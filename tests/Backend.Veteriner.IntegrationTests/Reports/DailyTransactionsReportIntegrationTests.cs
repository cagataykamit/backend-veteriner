using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// Gün sonu raporları (daily-transactions, cash-summary, end-of-day/open-items) uçtan uca: HTTP + gerçek SQL Server (LocalDB).
/// Her test kendi kiracısını kurar. Kanıtlananlar: olay sırası ve toplamlar, satışın çift sayılmaması, dünkü ödemenin bugün iptali,
/// düzeltmenin denetim kaydından gelmesi, ekran = CSV = XLSX, kasa özeti = döküm toplamı, kiracı izolasyonu, yetki, açık işlerin Bugün ile aynı kuraldan gelmesi.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed partial class DailyTransactionsReportIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Daily = "/api/v1/reports/daily-transactions";
    private const string Cash = "/api/v1/reports/cash-summary";
    private const string OpenItems = "/api/v1/reports/end-of-day/open-items";

    private static readonly string[] AdminPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Payments.Correct,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
        PermissionCatalog.Charges.Correct,
        PermissionCatalog.Sales.Read,
        PermissionCatalog.Sales.Create,
        PermissionCatalog.Sales.Correct,
        PermissionCatalog.Visits.Read,
    ];

    private static int _phoneSeed = 6_000_000;

    private readonly CustomWebApplicationFactory _factory;

    public DailyTransactionsReportIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static DateOnly Today => OperationDayBounds.ToLocalDate(DateTime.UtcNow);

    private static string Range(DateOnly? from = null, DateOnly? to = null)
        => $"from={(from ?? Today.AddDays(-1)):yyyy-MM-dd}&to={(to ?? Today.AddDays(1)):yyyy-MM-dd}";

    [Fact]
    public async Task Daily_Report_Should_List_Events_In_Time_Order_With_Totals_Sales_Voids_And_Corrections()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        var b = await env.SeedClientAsync("Berk Bal");
        var c = await env.SeedClientAsync("Ceren Can");
        var chargeA = await env.SeedChargeAsync(a, 600m, DateTime.UtcNow.AddMinutes(-50));
        var chargeB = await env.SeedChargeAsync(b, 200m, DateTime.UtcNow.AddMinutes(-40));
        var chargeC = await env.SeedChargeAsync(c, 300m, DateTime.UtcNow.AddMinutes(-30));
        var payA = await env.PayAsync(a, chargeA, 600m, PaymentMethod.Cash, DateTime.UtcNow.AddMinutes(-20));
        var payB = await env.PayAsync(b, chargeB, 200m, PaymentMethod.Card, DateTime.UtcNow.AddMinutes(-15));
        await env.SendAsync(HttpMethod.Put, $"/api/v1/charges/{chargeC}",
            new { description = "Duzeltilmis ucret", quantity = 1m, unitPrice = 350m, reason = "Fiyat listesi yanlis okundu" }, HttpStatusCode.OK);
        await env.SendAsync(HttpMethod.Post, $"/api/v1/payments/{payB}/void", new { reason = "Yanlis tahsilat girisi" }, HttpStatusCode.NoContent);
        await env.SellAsync(2m, 10m);

        var report = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range()}&pageSize=200");

        report.Currency.Should().Be("TRY");
        report.Items.Select(i => i.OccurredAtUtc).Should().BeInAscendingOrder();
        report.Items.Take(7).Select(i => i.Kind).Should().Equal(
            DailyTransactionKind.Charge, DailyTransactionKind.Charge, DailyTransactionKind.Charge,
            DailyTransactionKind.Payment, DailyTransactionKind.Payment,
            DailyTransactionKind.ChargeCorrection, DailyTransactionKind.PaymentVoid);
        report.Items.Skip(7).Select(i => i.Kind).Should().BeEquivalentTo(
            [DailyTransactionKind.Charge, DailyTransactionKind.Payment]);
        report.TotalCount.Should().Be(9);

        var t = report.Totals;
        t.GrossChargeAmount.Should().Be(1170m);   // 600 + 200 + 350 (düzeltilmiş güncel tutar) + 20 (satış)
        t.GrossChargeCount.Should().Be(4);
        t.SalesChargeAmount.Should().Be(20m);
        t.VoidedChargeAmount.Should().Be(0m);
        t.NetChargeAmount.Should().Be(1170m);
        t.CollectedAmount.Should().Be(820m);
        t.CollectedCount.Should().Be(3);
        t.SalesCollectedAmount.Should().Be(20m);
        t.VoidedCollectionAmount.Should().Be(200m);
        t.VoidedCollectionCount.Should().Be(1);
        t.NetCollectedAmount.Should().Be(620m);
        t.CorrectionCount.Should().Be(1);

        // Satış ayrı olay değil: ücreti ve tahsilatı işaretli (çift sayım yok).
        report.Items.Where(i => i.IsDirectSale).Select(i => i.Kind).Should().BeEquivalentTo(
            [DailyTransactionKind.Charge, DailyTransactionKind.Payment]);

        var correction = report.Items.Single(i => i.Kind == DailyTransactionKind.ChargeCorrection);
        correction.Amount.Should().BeNull();
        correction.Reason.Should().Be("Fiyat listesi yanlis okundu");
        correction.ReferenceId.Should().Be(chargeC);
        correction.ClientName.Should().Be("Ceren Can");
        correction.PerformedByUserId.Should().Be(env.UserId);

        var voided = report.Items.Single(i => i.Kind == DailyTransactionKind.PaymentVoid);
        voided.Reason.Should().Be("Yanlis tahsilat girisi");
        voided.Amount.Should().Be(200m);
        voided.PaymentMethod.Should().Be(PaymentMethod.Card);
        voided.ReferenceId.Should().Be(payB);
        voided.IsVoided.Should().BeTrue();
        voided.PerformedByUserId.Should().Be(env.UserId);

        // Alınan tahsilat satırı da iptal işaretli; işlemi yapan = tahsisi yapan kullanıcı.
        var receivedB = report.Items.Single(i => i.Kind == DailyTransactionKind.Payment && i.ReferenceId == payB);
        receivedB.IsVoided.Should().BeTrue();
        receivedB.PerformedByUserId.Should().Be(env.UserId);
        receivedB.PerformedByName.Should().NotBeNullOrEmpty();
        report.Items.Single(i => i.ReferenceId == payA && i.Kind == DailyTransactionKind.Payment).IsVoided.Should().BeFalse();
        var first = report.Items.First();
        first.ClientName.Should().Be("Aylin Acar");
        first.PerformedByUserId.Should().Be(env.UserId);
    }

    [Fact]
    public async Task Daily_Report_Should_Filter_And_Page_With_Totals_Of_The_Whole_Filtered_Set()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        for (var i = 0; i < 3; i++)
        {
            var charge = await env.SeedChargeAsync(a, 100m, DateTime.UtcNow.AddMinutes(-30 + i));
            await env.PayAsync(a, charge, 100m, i == 0 ? PaymentMethod.Card : PaymentMethod.Cash, DateTime.UtcNow.AddMinutes(-10 + i));
        }

        var page1 = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range()}&pageSize=4");
        var page2 = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range()}&pageSize=4&page=2");
        page1.TotalCount.Should().Be(6);
        page1.Items.Should().HaveCount(4);
        page2.Items.Should().HaveCount(2);
        page1.Items.Concat(page2.Items).Select(i => (i.Kind, i.ReferenceId)).Should().OnlyHaveUniqueItems();
        page2.Totals.Should().Be(page1.Totals);

        var cards = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range()}&kind=Payment&paymentMethod=Card");
        cards.TotalCount.Should().Be(1);
        cards.Totals.CollectedAmount.Should().Be(100m);
        cards.Totals.GrossChargeAmount.Should().Be(0m);

        var byOther = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range()}&performedByUserId={Guid.NewGuid()}");
        byOther.TotalCount.Should().Be(0);

        var payments = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range()}&kind=Payment");
        payments.Items.Should().OnlyContain(i => i.Kind == DailyTransactionKind.Payment);
        payments.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Yesterdays_Payment_Voided_Today_Should_Show_As_Void_Event_Today_And_Net_Negative()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        var paidAt = DateTime.UtcNow.AddDays(-2);
        var charge = await env.SeedChargeAsync(a, 300m, paidAt.AddMinutes(-5));
        var pay = await env.PayAsync(a, charge, 300m, PaymentMethod.Transfer, paidAt);
        await env.SendAsync(HttpMethod.Post, $"/api/v1/payments/{pay}/void", new { reason = "Dun alinan odeme iptal" }, HttpStatusCode.NoContent);
        var paidDay = OperationDayBounds.ToLocalDate(paidAt);

        var today = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range(Today, Today)}");
        today.Items.Should().ContainSingle().Which.Kind.Should().Be(DailyTransactionKind.PaymentVoid);
        today.Totals.CollectedAmount.Should().Be(0m);
        today.Totals.VoidedCollectionAmount.Should().Be(300m);
        today.Totals.NetCollectedAmount.Should().Be(-300m);

        var thatDay = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range(paidDay, paidDay)}");
        thatDay.Items.Select(i => i.Kind).Should().Equal(DailyTransactionKind.Charge, DailyTransactionKind.Payment);
        thatDay.Items.Last().IsVoided.Should().BeTrue();
        thatDay.Totals.NetCollectedAmount.Should().Be(300m);

        var cash = await env.GetAsync<CashSummaryReportDto>($"{Cash}?{Range(Today, Today)}");
        cash.Totals.NetAmount.Should().Be(-300m);
        cash.VoidedPayments.Should().ContainSingle().Which.PaidAtUtc.Should().BeCloseTo(paidAt, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Screen_Csv_And_Xlsx_Should_Agree_And_Cash_Summary_Should_Match_Daily_Totals()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        var b = await env.SeedClientAsync("Berk Bal");
        var chargeA = await env.SeedChargeAsync(a, 600m, DateTime.UtcNow.AddMinutes(-50));
        var chargeB = await env.SeedChargeAsync(b, 200.5m, DateTime.UtcNow.AddMinutes(-40));
        await env.PayAsync(a, chargeA, 600m, PaymentMethod.Cash, DateTime.UtcNow.AddMinutes(-20));
        var payB = await env.PayAsync(b, chargeB, 200.5m, PaymentMethod.Card, DateTime.UtcNow.AddMinutes(-15));
        await env.SendAsync(HttpMethod.Post, $"/api/v1/payments/{payB}/void", new { reason = "Yanlis tahsilat girisi" }, HttpStatusCode.NoContent);
        await env.SellAsync(2m, 10m);
        var query = Range();

        var screen = await env.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{query}&pageSize=200");
        var csv = await env.GetBytesAsync($"{Daily}/export?{query}", "text/csv");
        var xlsx = await env.GetBytesAsync($"{Daily}/export-xlsx?{query}", "spreadsheetml");

        var lines = Encoding.UTF8.GetString(csv).TrimEnd().Split(Environment.NewLine);
        lines.Should().HaveCount(screen.Items.Count + 1);
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        lines.Skip(1).Select(l => l.Split(';')[5]).Should()
            .Equal(screen.Items.Select(i => i.Amount is { } m ? m.ToString("0.00", tr) : string.Empty));
        lines.Skip(1).Select(l => l.Split(';')[1]).Should()
            .Equal(screen.Items.Select(i => DailyTransactionsReportColumnsLabel(i.Kind)));

        using var wb = new XLWorkbook(new MemoryStream(xlsx));
        var ws = wb.Worksheet(1);
        ws.LastRowUsed()!.RowNumber().Should().Be(screen.Items.Count + 1);
        Enumerable.Range(2, screen.Items.Count).Select(r => ws.Cell(r, 2).GetString()).Should()
            .Equal(lines.Skip(1).Select(l => l.Split(';')[1]));
        Enumerable.Range(2, screen.Items.Count).Select(r => ws.Cell(r, 6).IsEmpty() ? (decimal?)null : (decimal)ws.Cell(r, 6).GetDouble()).Should()
            .Equal(screen.Items.Select(i => i.Amount));

        // Kasa özeti = döküm toplamı (aynı mali olay kaynağı).
        var cash = await env.GetAsync<CashSummaryReportDto>($"{Cash}?{query}");
        cash.Totals.CollectedAmount.Should().Be(screen.Totals.CollectedAmount);
        cash.Totals.VoidedAmount.Should().Be(screen.Totals.VoidedCollectionAmount);
        cash.Totals.NetAmount.Should().Be(screen.Totals.NetCollectedAmount);
        cash.Totals.SalesCollectedAmount.Should().Be(screen.Totals.SalesCollectedAmount);
        cash.ByMethod.Select(m => m.PaymentMethod).Should().Equal(PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Transfer);
        cash.ByMethod[0].NetAmount.Should().Be(620m);
        cash.ByMethod[1].CollectedAmount.Should().Be(200.5m);
        cash.ByMethod[1].VoidedAmount.Should().Be(200.5m);
        cash.ByMethod[2].NetAmount.Should().Be(0m);
        cash.ByDay.Sum(d => d.NetAmount).Should().Be(cash.Totals.NetAmount);
        cash.ByUser.Should().ContainSingle().Which.UserId.Should().Be(env.UserId);
        cash.ByUser[0].CollectedAmount.Should().Be(820.5m);
        cash.ByUser[0].VoidedAmount.Should().Be(200.5m);
        cash.VoidedPayments.Should().ContainSingle().Which.Reason.Should().Be("Yanlis tahsilat girisi");

        var cashCsv = Encoding.UTF8.GetString(await env.GetBytesAsync($"{Cash}/export?{query}", "text/csv")).TrimEnd().Split(Environment.NewLine);
        cashCsv[0].Should().Be("﻿Bölüm;Ad;Tahsilat Adedi;Tahsilat Tutarı;İptal Adedi;İptal Tutarı;Net");
        cashCsv[1].Should().Be($"Toplam;Toplam;3;{cash.Totals.CollectedAmount.ToString("0.00", tr)};1;200,50;{cash.Totals.NetAmount.ToString("0.00", tr)}");
        using var cashWb = new XLWorkbook(new MemoryStream(await env.GetBytesAsync($"{Cash}/export-xlsx?{query}", "spreadsheetml")));
        cashWb.Worksheet(1).LastRowUsed()!.RowNumber().Should().Be(cashCsv.Length);
    }

    [Fact]
    public async Task Reports_Should_Not_Leak_Other_Tenants_And_Should_Enforce_Permissions_And_Ranges()
    {
        var env = await SeedTenantAsync();
        var other = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        var charge = await env.SeedChargeAsync(a, 100m, DateTime.UtcNow.AddMinutes(-5));
        await env.PayAsync(a, charge, 100m, PaymentMethod.Cash, DateTime.UtcNow.AddMinutes(-3));

        (await other.GetAsync<DailyTransactionsReportResultDto>($"{Daily}?{Range()}")).TotalCount.Should().Be(0);
        (await other.GetAsync<CashSummaryReportDto>($"{Cash}?{Range()}")).Totals.CollectedAmount.Should().Be(0m);
        var foreignClinic = await other.Http.GetAsync($"{Daily}?{Range()}&clinicId={env.ClinicId}");
        foreignClinic.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        var anonymous = _factory.CreateClient();
        var paymentsOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Payments.Read]);
        var chargesOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Charges.Read]);
        var noVisits = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Payments.Read, PermissionCatalog.Charges.Read]);
        foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
        {
            (await anonymous.GetAsync($"{Daily}{suffix}?{Range()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await paymentsOnly.GetAsync($"{Daily}{suffix}?{Range()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await chargesOnly.GetAsync($"{Daily}{suffix}?{Range()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await noVisits.GetAsync($"{Daily}{suffix}?{Range()}")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await anonymous.GetAsync($"{Cash}{suffix}?{Range()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await chargesOnly.GetAsync($"{Cash}{suffix}?{Range()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await paymentsOnly.GetAsync($"{Cash}{suffix}?{Range()}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await noVisits.GetAsync($"{OpenItems}?{Range()}&clinicId={env.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var reversed = await env.Http.GetAsync($"{Daily}?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}");
        reversed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(reversed)).Should().Be("Registers.DateRangeInvalid");
        var tooLong = await env.Http.GetAsync($"{Cash}?from={Today:yyyy-MM-dd}&to={Today.AddDays(400):yyyy-MM-dd}");
        (await IntegrationTestProblemDetails.ReadCodeAsync(tooLong)).Should().Be("Registers.RangeTooLong");
    }

    [Fact]
    public async Task End_Of_Day_Open_Items_Should_Use_The_Shared_Rule_And_Require_A_Clinic()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Aylin Acar");
        var pet = await env.SeedPetAsync(a);
        var visit = await env.SeedVisitAsync(pet);
        await env.SeedChargeAsync(a, 250m, DateTime.UtcNow.AddMinutes(-5), visit);

        var missingClinic = await env.Http.GetAsync($"{OpenItems}?{Range(Today, Today)}");
        missingClinic.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(missingClinic)).Should().Be("EndOfDay.ClinicScopeRequired");

        var report = await env.GetAsync<EndOfDayOpenItemsDto>($"{OpenItems}?{Range(Today, Today)}&clinicId={env.ClinicId}");
        var todayView = await env.Http.GetFromJsonAsync<TodayDto>($"/api/v1/visits/today?clinicId={env.ClinicId}");

        report.ClinicId.Should().Be(env.ClinicId);
        // Açık geliş bugün gelmiş: gün sonu penceresinde sayılır, Bugün'ün "önceki günden" kuralında sayılmaz.
        report.OpenItems.IncompleteVisitCount.Should().Be(1);
        todayView!.OpenItems!.IncompleteVisitCount.Should().Be(0);
        // Klinik geneli güncel durum: Bugün ile aynı arayüzden aynı sayı.
        report.OpenItems.AwaitingLabCount.Should().Be(todayView.OpenItems.AwaitingLabCount);
        report.OpenItems.OutstandingVisitCount.Should().Be(todayView.OpenItems.OutstandingVisitCount);
        report.OpenItems.OutstandingAmount.Should().Be(todayView.OpenItems.OutstandingAmount);

        // Pencere dışı gün: o günün ücretsiz ziyareti yok.
        var past = await env.GetAsync<EndOfDayOpenItemsDto>($"{OpenItems}?{Range(Today.AddDays(-10), Today.AddDays(-9))}&clinicId={env.ClinicId}");
        past.OpenItems.UnchargedVisitCount.Should().Be(0);
    }

    private static string DailyTransactionsReportColumnsLabel(DailyTransactionKind kind)
        => kind switch
        {
            DailyTransactionKind.Charge => "Ücret",
            DailyTransactionKind.ChargeVoid => "Ücret iptali",
            DailyTransactionKind.ChargeCorrection => "Ücret düzeltmesi",
            DailyTransactionKind.Payment => "Tahsilat",
            DailyTransactionKind.PaymentVoid => "Tahsilat iptali",
            _ => "Tahsilat düzeltmesi",
        };

    // ---- Kurulum ----

    private async Task<Env> SeedTenantAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid userId;
        Guid productId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Dtr-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
            db.Tenants.Add(tenant);
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
            var clinic = new Clinic(tenant.Id, $"Dtr-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            var product = new Product(tenant.Id, $"dp-{Guid.NewGuid():N}"[..14], "Adet", 10.00m, "TRY", DateTime.UtcNow, sku: $"D1-{Guid.NewGuid():N}"[..14]);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, product.Id, 100m, 0, DateTime.UtcNow));
            await db.SaveChangesAsync();

            (userId, email) = await Env.SeedUserAsync(db, hasher, tenant.Id, clinic.Id);
            tenantId = tenant.Id;
            clinicId = clinic.Id;
            productId = product.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, AdminPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Env(_factory.Services, hasher, _factory.CreateClient, http, tenantId, clinicId, userId, productId);
    }

    private sealed class Env
    {
        private readonly IServiceProvider _services;
        private readonly IPasswordHasher _hasher;
        private readonly Func<HttpClient> _createClient;
        private readonly Guid _productId;

        public Env(IServiceProvider services, IPasswordHasher hasher, Func<HttpClient> createClient, HttpClient http,
            Guid tenantId, Guid clinicId, Guid userId, Guid productId)
        {
            _services = services;
            _hasher = hasher;
            _createClient = createClient;
            _productId = productId;
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
            var email = $"dtr-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinicId, DateTime.UtcNow));
            await db.SaveChangesAsync();
            return (user.Id, email);
        }

        public async Task<HttpClient> NewHttpForAnotherUserAsync(string[] permissions)
        {
            string email;
            await using (var scope = _services.CreateSystemScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (_, email) = await SeedUserAsync(db, _hasher, TenantId, ClinicId);
            }

            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_services, email, permissions);
            var http = _createClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        public async Task<T> GetAsync<T>(string url)
        {
            var response = await Http.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }

        public async Task<byte[]> GetBytesAsync(string url, string contentTypePart)
        {
            var response = await Http.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            response.Content.Headers.ContentType!.MediaType.Should().Contain(contentTypePart);
            return await response.Content.ReadAsByteArrayAsync();
        }

        public async Task SendAsync(HttpMethod method, string url, object body, HttpStatusCode expected)
        {
            var response = await Http.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) });
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        }

        public async Task<Guid> PayAsync(Guid clientId, Guid chargeId, decimal amount, PaymentMethod method, DateTime paidAtUtc)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/payments", new
            {
                ClinicId,
                ClientId = clientId,
                Amount = amount,
                Currency = "TRY",
                Method = method,
                PaidAtUtc = paidAtUtc,
                RequestId = Guid.NewGuid(),
                Allocations = new[] { new { ChargeId = chargeId, Amount = amount } },
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Guid>();
        }

        /// <summary>Peşin müşteriye nakit ödemeli doğrudan satış; satış kimliğini döner.</summary>
        public async Task<string> SellAsync(decimal quantity, decimal unitPrice)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/sales", new
            {
                clinicId = ClinicId,
                requestId = Guid.NewGuid(),
                lines = new[] { new { productId = _productId, quantity, unitPrice } },
                payment = new { method = "Cash" },
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadAsStringAsync());
        }

        public async Task<Guid> SeedClientAsync(string name)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var phone = $"90555{Interlocked.Increment(ref _phoneSeed):D7}";
            var client = new Client(TenantId, name, DateTime.UtcNow, phone);
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedPetAsync(Guid clientId)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, clientId, $"DPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task<Guid> SeedVisitAsync(Guid petId)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = new Visit(TenantId, ClinicId, petId, null, null, UserId, DateTime.UtcNow);
            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            return visit.Id;
        }

        public async Task<Guid> SeedChargeAsync(Guid clientId, decimal amount, DateTime createdAtUtc, Guid? visitId = null)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = Charge.Create(
                    TenantId, ClinicId, clientId, null, visitId, null,
                    "Seed ucret", 1m, amount, UserId, createdAtUtc, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }
    }
}
