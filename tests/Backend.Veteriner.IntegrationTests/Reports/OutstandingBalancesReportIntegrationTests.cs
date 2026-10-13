using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// GET /reports/outstanding-balances (+ export, export-xlsx) uçtan uca: HTTP + gerçek SQL Server (LocalDB, izole test veritabanı).
/// Her test kendi kiracısını kurar; rapor kiracı geneli toplam verdiği için diğer testlerin verisi sonucu etkilemez.
/// Kanıtlananlar: sıralama + toplam + sayfalama, bakiye ucu ve Bugün ile birebir eşitlik, iptal edilen ücret/ödeme,
/// ekran = CSV = XLSX, klinik kapsamı, kiracı izolasyonu (IDOR), yetki.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class OutstandingBalancesReportIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/reports/outstanding-balances";

    private static readonly string[] AdminPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Payments.Correct,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
        PermissionCatalog.Visits.Read,
    ];

    private static int _phoneSeed = 5_000_000;

    private readonly CustomWebApplicationFactory _factory;

    public OutstandingBalancesReportIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Report_Should_Order_By_Outstanding_Desc_Exclude_Closed_And_Voided_And_Match_Balance_And_Today()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        var b = await env.SeedClientAsync("Berk Bal");
        var c = await env.SeedClientAsync("Ceren Can");
        var d = await env.SeedClientAsync("Deniz Dal");
        await env.SeedClientAsync("Ece Eren");

        var petA = await env.SeedPetAsync(a);
        var visitA = await env.SeedVisitAsync(petA);
        var oldest = DateTime.UtcNow.AddDays(-40);
        var a1 = await env.SeedChargeAsync(a, 1000m, petId: petA, visitId: visitA, createdAtUtc: DateTime.UtcNow.AddDays(-10));
        await env.SeedChargeAsync(a, 50m, createdAtUtc: oldest);
        await env.SeedChargeAsync(b, 90m);
        var cCharge = await env.SeedChargeAsync(c, 200m);
        var dVoided = await env.SeedChargeAsync(d, 500m);
        await env.PayAsync(a, a1, 400m);
        await env.PayAsync(c, cCharge, 200m);
        await env.VoidChargeAsync(dVoided);

        var report = await env.GetReportAsync();

        report.Currency.Should().Be("TRY");
        report.TotalCount.Should().Be(2);
        report.TotalOutstandingAmount.Should().Be(740m);
        report.Items.Select(i => i.ClientId).Should().Equal(a, b);
        var first = report.Items[0];
        first.OutstandingAmount.Should().Be(650m);
        first.OpenChargeCount.Should().Be(2);
        first.OldestOpenChargeAtUtc.Should().BeCloseTo(oldest, TimeSpan.FromSeconds(2));
        first.ClientName.Should().Be("Aylin Acar");
        first.Phone.Should().NotBeNullOrEmpty();
        report.Items[1].OutstandingAmount.Should().Be(90m);

        // Aynı kural: rapor satırı = müşteri bakiyesi ucu = Bugün satırındaki clientOutstandingAmount.
        foreach (var item in report.Items)
            (await env.GetBalanceAsync(item.ClientId)).OutstandingAmount.Should().Be(item.OutstandingAmount);
        var today = await env.Http.GetFromJsonAsync<TodayDto>($"/api/v1/visits/today?clinicId={env.ClinicId}");
        today!.Items.Single(i => i.VisitId == visitA).ClientOutstandingAmount.Should().Be(first.OutstandingAmount);

        // A kalanını öderse listeden çıkar.
        await env.PayAsync(a, a1, 600m);
        var a2 = (await env.GetBalanceAsync(a)).Pets.SelectMany(p => p.Visits).SelectMany(v => v.Charges).Single();
        await env.PayAsync(a, a2.ChargeId, 50m);
        var after = await env.GetReportAsync();
        after.Items.Select(i => i.ClientId).Should().Equal(b);
        after.TotalOutstandingAmount.Should().Be(90m);
    }

    [Fact]
    public async Task Voided_Payment_Should_Reopen_The_Debt_In_Report()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        var charge = await env.SeedChargeAsync(a, 300m);
        var paymentId = await env.PayAsync(a, charge, 300m);
        (await env.GetReportAsync()).Items.Should().BeEmpty();

        var voided = await env.Http.PostAsJsonAsync($"/api/v1/payments/{paymentId}/void", new { Reason = "Yanlis tahsilat girisi" });
        voided.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var report = await env.GetReportAsync();
        report.Items.Should().ContainSingle().Which.OutstandingAmount.Should().Be(300m);
        (await env.GetBalanceAsync(a)).OutstandingAmount.Should().Be(300m);
    }

    [Fact]
    public async Task Legacy_Unallocated_Payment_Should_Not_Reduce_The_Reported_Debt()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        await env.SeedChargeAsync(a, 500m);
        await env.SeedLegacyPaymentAsync(a, 120m);

        var report = await env.GetReportAsync();

        report.Items.Should().ContainSingle().Which.OutstandingAmount.Should().Be(500m);
        report.TotalOutstandingAmount.Should().Be(500m);
    }

    [Fact]
    public async Task Report_Should_Page_Filter_By_MinAmount_And_Search_With_Stable_Totals()
    {
        var env = await SeedTenantAsync();
        var names = new[] { "Ayse Yildiz", "Burak Yilmaz", "Cem Kaya", "Derya Demir", "Emre Ak" };
        var amounts = new[] { 500m, 400m, 300m, 200m, 100m };
        var ids = new List<Guid>();
        for (var i = 0; i < names.Length; i++)
        {
            var id = await env.SeedClientAsync(names[i]);
            await env.SeedChargeAsync(id, amounts[i]);
            ids.Add(id);
        }

        var page1 = await env.GetReportAsync("page=1&pageSize=2");
        var page2 = await env.GetReportAsync("page=2&pageSize=2");
        var page3 = await env.GetReportAsync("page=3&pageSize=2");
        page1.Items.Select(i => i.ClientId).Should().Equal(ids[0], ids[1]);
        page2.Items.Select(i => i.ClientId).Should().Equal(ids[2], ids[3]);
        page3.Items.Select(i => i.ClientId).Should().Equal(ids[4]);
        foreach (var p in new[] { page1, page2, page3 })
        {
            p.TotalCount.Should().Be(5);
            p.TotalOutstandingAmount.Should().Be(1500m);
        }

        var min = await env.GetReportAsync("minAmount=300");
        min.TotalCount.Should().Be(3);
        min.TotalOutstandingAmount.Should().Be(1200m);
        (await env.GetReportAsync("minAmount=301")).TotalCount.Should().Be(2);

        var byName = await env.GetReportAsync("search=" + Uri.EscapeDataString("yilmaz"));
        byName.Items.Should().ContainSingle().Which.ClientId.Should().Be(ids[1]);
        byName.TotalOutstandingAmount.Should().Be(400m);

        var phone = (await env.GetReportAsync()).Items[2].Phone!;
        var byPhone = await env.GetReportAsync("search=" + Uri.EscapeDataString("0" + phone[2..]));
        byPhone.Items.Should().ContainSingle().Which.ClientId.Should().Be(ids[2]);

        (await env.GetReportAsync("search=zzzz-yok")).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Screen_Csv_And_Xlsx_Should_Return_Same_Clients_In_Same_Order()
    {
        var env = await SeedTenantAsync();
        var names = new[] { "Ayse Yildiz", "Burak Yilmaz", "Cem Kaya", "Derya Demir" };
        var amounts = new[] { 125.5m, 400m, 300m, 40m };
        for (var i = 0; i < names.Length; i++)
            await env.SeedChargeAsync(await env.SeedClientAsync(names[i]), amounts[i]);

        const string filter = "minAmount=100";
        var screen = await env.GetReportAsync(filter);
        screen.Items.Should().HaveCount(3);

        var csvResponse = await env.Http.GetAsync($"{Url}/export?{filter}");
        csvResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        csvResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        csvResponse.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"^borc-raporu-\d{8}\.csv$");
        var csvBytes = await csvResponse.Content.ReadAsByteArrayAsync();
        csvBytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var csvRows = Encoding.UTF8.GetString(csvBytes).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(l => l.Split(';')).ToList();
        csvRows.Select(r => r[0]).Should().Equal(screen.Items.Select(i => i.ClientName));
        csvRows.Select(r => decimal.Parse(r[2], CultureInfo.GetCultureInfo("tr-TR")))
            .Should().Equal(screen.Items.Select(i => i.OutstandingAmount));
        csvRows.Select(r => r[1]).Should().Equal(screen.Items.Select(i => i.Phone ?? string.Empty));

        var xlsxResponse = await env.Http.GetAsync($"{Url}/export-xlsx?{filter}");
        xlsxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsxResponse.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"^borc-raporu-\d{8}\.xlsx$");
        using var wb = new XLWorkbook(new MemoryStream(await xlsxResponse.Content.ReadAsByteArrayAsync()));
        var ws = wb.Worksheet(1);
        var xlsxRows = Enumerable.Range(2, ws.LastRowUsed()!.RowNumber() - 1).ToList();
        xlsxRows.Select(r => ws.Cell(r, 1).GetString()).Should().Equal(screen.Items.Select(i => i.ClientName));
        xlsxRows.Select(r => (decimal)ws.Cell(r, 3).GetDouble()).Should().Equal(screen.Items.Select(i => i.OutstandingAmount));

        // Eşleşme yoksa yalnız başlık satırı.
        var empty = await env.Http.GetAsync($"{Url}/export?search=zzzz-yok");
        Encoding.UTF8.GetString(await empty.Content.ReadAsByteArrayAsync()).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1);
    }

    [Fact]
    public async Task Report_Should_Only_Count_Clinics_Assigned_To_The_User()
    {
        var env = await SeedTenantAsync();
        var clinicB = await env.SeedClinicAsync();
        var x = await env.SeedClientAsync("Aylin Acar");
        var y = await env.SeedClientAsync("Berk Bal");
        await env.SeedChargeAsync(x, 100m);
        await env.SeedChargeAsync(x, 900m, clinicB);
        await env.SeedChargeAsync(y, 70m, clinicB);

        var report = await env.GetReportAsync();

        report.Items.Should().ContainSingle().Which.ClientId.Should().Be(x);
        report.TotalOutstandingAmount.Should().Be(100m);
        (await env.GetReportAsync($"clinicId={env.ClinicId}")).TotalOutstandingAmount.Should().Be(100m);
        var foreign = await env.Http.GetAsync($"{Url}?clinicId={clinicB}");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(foreign)).Should().Be("Clinics.AccessDenied");
        (await env.Http.GetAsync($"{Url}/export?clinicId={clinicB}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await env.Http.GetAsync($"{Url}/export-xlsx?clinicId={clinicB}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Report_Should_Not_Expose_Other_Tenant_Debts()
    {
        var one = await SeedTenantAsync();
        var two = await SeedTenantAsync();
        var c1 = await one.SeedClientAsync("Birinci Musteri");
        var c2 = await two.SeedClientAsync("Ikinci Musteri");
        await one.SeedChargeAsync(c1, 111m);
        await two.SeedChargeAsync(c2, 222m);

        var r1 = await one.GetReportAsync();
        var r2 = await two.GetReportAsync();

        r1.Items.Select(i => i.ClientId).Should().Equal(c1);
        r1.TotalOutstandingAmount.Should().Be(111m);
        r2.Items.Select(i => i.ClientId).Should().Equal(c2);
        r2.TotalOutstandingAmount.Should().Be(222m);

        // Diğer kiracının klinik kimliği ile istek veri döndürmez.
        foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
        {
            var cross = await one.Http.GetAsync($"{Url}{suffix}?clinicId={two.ClinicId}");
            cross.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        }

        // Diğer kiracının müşterisi aramayla da bulunamaz.
        (await one.GetReportAsync("search=" + Uri.EscapeDataString("Ikinci"))).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Report_Needs_Charges_Read_And_Works_With_Read_Only_Permission()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        await env.SeedChargeAsync(a, 10m);
        var anonymous = _factory.CreateClient();
        var paymentsOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Payments.Read, PermissionCatalog.Clients.Read]);
        var readOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Charges.Read]);

        foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
        {
            (await anonymous.GetAsync($"{Url}{suffix}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await paymentsOnly.GetAsync($"{Url}{suffix}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await readOnly.GetAsync($"{Url}{suffix}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var ok = await readOnly.GetFromJsonAsync<OutstandingBalancesReportResultDto>(Url);
        ok!.TotalOutstandingAmount.Should().Be(10m);
    }

    [Fact]
    public async Task Report_Should_Return_400_For_Negative_MinAmount_And_403_For_Clinic_Conflict()
    {
        var env = await SeedTenantAsync();

        var negative = await env.Http.GetAsync($"{Url}?minAmount=-1");
        negative.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(negative)).Should().Be("OutstandingBalances.MinAmountInvalid");
        (await env.Http.GetAsync($"{Url}/export?minAmount=-1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var clinicB = await env.SeedClinicAsync();
        var scoped = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Charges.Read], clinicContext: env.ClinicId);
        var mismatch = await scoped.GetAsync($"{Url}?clinicId={clinicB}");
        mismatch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(mismatch)).Should().Be("Context.ClinicConflict");
    }

    // ---- Kurulum ----

    private async Task<Env> SeedTenantAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Rpt-{Guid.NewGuid():N}"[..18]);
            db.Tenants.Add(tenant);
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
            var clinic = new Clinic(tenant.Id, $"Rpt-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            (userId, email) = await Env.SeedUserAsync(db, hasher, tenant.Id, clinic.Id);
            tenantId = tenant.Id;
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, AdminPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Env(_factory.Services, hasher, _factory.CreateClient, http, tenantId, clinicId, userId);
    }

    private sealed class Env
    {
        private readonly IServiceProvider _services;
        private readonly IPasswordHasher _hasher;
        private readonly Func<HttpClient> _createClient;

        public Env(IServiceProvider services, IPasswordHasher hasher, Func<HttpClient> createClient, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
        {
            _services = services;
            _hasher = hasher;
            _createClient = createClient;
            Http = http;
            TenantId = tenantId;
            ClinicId = clinicId;
            UserId = userId;
        }

        public HttpClient Http { get; }
        public Guid TenantId { get; }
        public Guid ClinicId { get; }
        public Guid UserId { get; }

        public static async Task<(Guid UserId, string Email)> SeedUserAsync(
            AppDbContext db, IPasswordHasher hasher, Guid tenantId, Guid clinicId)
        {
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            var email = $"rpt-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenantId));
            db.UserClinics.Add(new UserClinic(user.Id, clinicId));
            await db.SaveChangesAsync();
            return (user.Id, email);
        }

        public async Task<HttpClient> NewHttpForAnotherUserAsync(string[] permissions, Guid? clinicContext = null)
        {
            string email;
            await using (var scope = _services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (_, email) = await SeedUserAsync(db, _hasher, TenantId, ClinicId);
            }

            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_services, email, permissions, clinicContext);
            var http = _createClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        public async Task<OutstandingBalancesReportResultDto> GetReportAsync(string? query = null)
        {
            var response = await Http.GetAsync(query is null ? Url : $"{Url}?{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<OutstandingBalancesReportResultDto>())!;
        }

        public async Task<ClientBalanceDto> GetBalanceAsync(Guid clientId)
            => (await Http.GetFromJsonAsync<ClientBalanceDto>($"/api/v1/clients/{clientId}/balance"))!;

        public async Task<Guid> PayAsync(Guid clientId, Guid chargeId, decimal amount)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/payments", new
            {
                ClinicId,
                ClientId = clientId,
                Amount = amount,
                Currency = "TRY",
                Method = PaymentMethod.Cash,
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-1),
                RequestId = Guid.NewGuid(),
                Allocations = new[] { new { ChargeId = chargeId, Amount = amount } },
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return await response.Content.ReadFromJsonAsync<Guid>();
        }

        public async Task<Guid> SeedClientAsync(string name)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var phone = $"90555{Interlocked.Increment(ref _phoneSeed):D7}";
            var client = new Client(TenantId, name, phone);
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedPetAsync(Guid clientId)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, clientId, $"RPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task<Guid> SeedVisitAsync(Guid petId)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = new Visit(TenantId, ClinicId, petId, null, null, UserId, DateTime.UtcNow);
            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            return visit.Id;
        }

        public async Task<Guid> SeedClinicAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            return clinic.Id;
        }

        public async Task<Guid> SeedChargeAsync(
            Guid clientId, decimal amount, Guid? clinicId = null, Guid? petId = null, Guid? visitId = null, DateTime? createdAtUtc = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = Charge.Create(
                    TenantId, clinicId ?? ClinicId, clientId, petId, visitId, null,
                    "Seed ucret", 1m, amount, UserId, createdAtUtc ?? DateTime.UtcNow, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }

        public async Task VoidChargeAsync(Guid chargeId)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = await db.Charges.SingleAsync(c => c.Id == chargeId);
            charge.Void(UserId, "Test iptali", DateTime.UtcNow).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        public async Task SeedLegacyPaymentAsync(Guid clientId, decimal amount)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Payments.Add(new Payment(
                TenantId, ClinicId, clientId, null, null, null, amount, "TRY", PaymentMethod.Cash,
                DateTime.UtcNow.AddMinutes(-5), "Eski kayit"));
            await db.SaveChangesAsync();
        }
    }
}
