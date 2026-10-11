using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
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
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Clients;

/// <summary>
/// GET /clients/{id}/balance uçtan uca: HTTP + gerçek SQL Server (LocalDB, izole test veritabanı).
/// Kabul senaryosu (1.000 TL / 400 + 600), hasta-ziyaret kırılımı, bağlanmamış eski ödeme, sayaç–tahsis tutarlılığı,
/// klinik ve kiracı izolasyonu, yetki. Ayrıca GET /visits/today satırlarındaki ücret/ödenen/kalan alanları.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClientBalanceIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AdminPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
        PermissionCatalog.Visits.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public ClientBalanceIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Charge_1000_Paid_400_Then_600_Should_Show_600_Then_Zero_And_Match_Allocation_Rows()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.SeedPetAsync();
        var visitId = await ctx.SeedVisitAsync(petId);
        var chargeId = await ctx.SeedChargeAsync(1000m, petId, visitId);

        var before = await ctx.GetBalanceAsync();
        before.ChargedAmount.Should().Be(1000m);
        before.OutstandingAmount.Should().Be(1000m);

        (await ctx.PayAsync(400m, chargeId)).StatusCode.Should().Be(HttpStatusCode.Created);
        var partial = await ctx.GetBalanceAsync();
        partial.ChargedAmount.Should().Be(1000m);
        partial.AllocatedAmount.Should().Be(400m);
        partial.OutstandingAmount.Should().Be(600m);
        partial.OpenChargeCount.Should().Be(1);
        partial.ClinicId.Should().BeNull("kullanıcıda aktif klinik bağlamı yok; atanmış tüm klinikler sayılır");
        var petGroup = partial.Pets.Should().ContainSingle().Subject;
        petGroup.PetId.Should().Be(petId);
        petGroup.OutstandingAmount.Should().Be(600m);
        var visitGroup = petGroup.Visits.Should().ContainSingle().Subject;
        visitGroup.VisitId.Should().Be(visitId);
        visitGroup.VisitArrivedAtUtc.Should().NotBeNull();
        var line = visitGroup.Charges.Should().ContainSingle().Subject;
        line.ChargeId.Should().Be(chargeId);
        line.Amount.Should().Be(1000m);
        line.AllocatedAmount.Should().Be(400m);
        line.OutstandingAmount.Should().Be(600m);

        (await ctx.PayAsync(600m, chargeId)).StatusCode.Should().Be(HttpStatusCode.Created);
        var closed = await ctx.GetBalanceAsync();
        closed.ChargedAmount.Should().Be(1000m);
        closed.AllocatedAmount.Should().Be(1000m);
        closed.OutstandingAmount.Should().Be(0m);
        closed.OpenChargeCount.Should().Be(0);
        closed.Pets.Should().BeEmpty();

        // Doğrulama sorgusu: bakiyedeki tahsis toplamı, tahsis satırlarının (PaymentAllocations) toplamıdır.
        (await ctx.SumAllocationRowsAsync()).Should().Be(closed.AllocatedAmount);
    }

    [Fact]
    public async Task Balance_Should_Group_Charges_Without_Pet_Or_Visit_And_Sum_Across_Charges()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.SeedPetAsync();
        await ctx.SeedChargeAsync(300m, petId, null);
        await ctx.SeedChargeAsync(80m, null, null);

        var balance = await ctx.GetBalanceAsync();

        balance.OutstandingAmount.Should().Be(380m);
        balance.OpenChargeCount.Should().Be(2);
        balance.Pets.Select(p => p.PetId).Should().Equal(petId, null);
        balance.Pets[0].Visits.Should().ContainSingle().Which.VisitId.Should().BeNull();
    }

    [Fact]
    public async Task Legacy_Unallocated_Payment_Should_Appear_Separately_And_Not_Change_Outstanding()
    {
        var ctx = await SeedAsync();
        await ctx.SeedChargeAsync(500m, null, null);
        await ctx.SeedLegacyPaymentAsync(120m);

        var balance = await ctx.GetBalanceAsync();

        balance.OutstandingAmount.Should().Be(500m);
        balance.UnallocatedPayments.TotalAmount.Should().Be(120m);
        balance.UnallocatedPayments.PaymentCount.Should().Be(1);
        balance.UnallocatedPayments.Items.Should().ContainSingle().Which.UnallocatedAmount.Should().Be(120m);
    }

    [Fact]
    public async Task Payment_Allocated_To_A_Charge_Should_Not_Appear_As_Unallocated()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(200m, null, null);
        (await ctx.PayAsync(200m, chargeId)).StatusCode.Should().Be(HttpStatusCode.Created);

        var balance = await ctx.GetBalanceAsync();

        balance.UnallocatedPayments.PaymentCount.Should().Be(0);
        balance.UnallocatedPayments.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Client_Without_Charges_Should_Return_200_With_Zeros()
    {
        var ctx = await SeedAsync();

        var balance = await ctx.GetBalanceAsync();

        balance.ClientId.Should().Be(ctx.ClientId);
        balance.Currency.Should().Be("TRY");
        balance.ChargedAmount.Should().Be(0m);
        balance.Pets.Should().BeEmpty();
    }

    [Fact]
    public async Task Charges_In_Unassigned_Clinic_Should_Not_Be_Counted()
    {
        var ctx = await SeedAsync();
        await ctx.SeedChargeAsync(100m, null, null);
        var foreignClinicId = await ctx.SeedForeignClinicAsync();
        await ctx.SeedChargeAsync(900m, null, null, foreignClinicId);
        await ctx.SeedLegacyPaymentAsync(55m, foreignClinicId);

        var balance = await ctx.GetBalanceAsync();

        balance.ChargedAmount.Should().Be(100m);
        balance.OutstandingAmount.Should().Be(100m);
        balance.UnallocatedPayments.PaymentCount.Should().Be(0);
    }

    [Fact]
    public async Task Client_Of_Another_Tenant_Or_Unknown_Should_Return_404()
    {
        var ctx = await SeedAsync();
        var foreignClientId = await ctx.SeedForeignTenantClientAsync();

        (await ctx.Http.GetAsync($"/api/v1/clients/{foreignClientId}/balance")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.GetAsync($"/api/v1/clients/{Guid.NewGuid()}/balance")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Balance_Needs_Charges_Read_And_Works_With_Read_Only_Permission()
    {
        var ctx = await SeedAsync();
        await ctx.SeedChargeAsync(10m, null, null);
        var noRead = await ctx.NewHttpForAnotherUserAsync([PermissionCatalog.Clients.Read, PermissionCatalog.Payments.Read]);
        var readOnly = await ctx.NewHttpForAnotherUserAsync([PermissionCatalog.Charges.Read]);

        (await noRead.GetAsync($"/api/v1/clients/{ctx.ClientId}/balance")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ok = await readOnly.GetAsync($"/api/v1/clients/{ctx.ClientId}/balance");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<ClientBalanceDto>())!.OutstandingAmount.Should().Be(10m);
    }

    // ---- Bugün satırı ----

    [Fact]
    public async Task Today_Should_Show_Partially_Paid_And_Unpaid_Visit_Amounts_And_Client_Outstanding()
    {
        var ctx = await SeedAsync();
        var pet1 = await ctx.SeedPetAsync();
        var pet2 = await ctx.SeedPetAsync();
        var visit1 = await ctx.SeedVisitAsync(pet1);
        var visit2 = await ctx.SeedVisitAsync(pet2);
        var charge1 = await ctx.SeedChargeAsync(1000m, pet1, visit1);
        await ctx.SeedChargeAsync(90m, pet2, visit2);
        await ctx.SeedChargeAsync(50m, null, null);
        (await ctx.PayAsync(400m, charge1)).StatusCode.Should().Be(HttpStatusCode.Created);

        var today = await ctx.GetTodayAsync();

        var partial = today.Items.Single(i => i.VisitId == visit1);
        (partial.ChargedAmount, partial.AllocatedAmount, partial.OutstandingAmount).Should().Be((1000m, 400m, 600m));
        partial.ClientOutstandingAmount.Should().Be(740m);
        var unpaid = today.Items.Single(i => i.VisitId == visit2);
        (unpaid.ChargedAmount, unpaid.AllocatedAmount, unpaid.OutstandingAmount).Should().Be((90m, 0m, 90m));
        unpaid.ClientOutstandingAmount.Should().Be(740m);
        // Mevcut gösterge aynen kalır (alanlar eklemedir).
        Enum.IsDefined(partial.PaymentIndicator).Should().BeTrue();
    }

    [Fact]
    public async Task Today_Visit_Without_Charges_Should_Show_Zero_Amounts()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.SeedVisitAsync(await ctx.SeedPetAsync());

        var item = (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId);

        (item.ChargedAmount, item.AllocatedAmount, item.OutstandingAmount, item.ClientOutstandingAmount)
            .Should().Be((0m, 0m, 0m, 0m));
    }

    [Fact]
    public async Task Today_Client_Outstanding_Should_Ignore_Other_Clinic_Charges()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.SeedVisitAsync(await ctx.SeedPetAsync());
        await ctx.SeedChargeAsync(100m, null, null);
        var otherClinicId = await ctx.SeedForeignClinicAsync();
        await ctx.SeedChargeAsync(900m, null, null, otherClinicId);

        var item = (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId);

        item.ClientOutstandingAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Today_Without_Charges_Read_Should_Return_Null_Amounts()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.SeedVisitAsync(await ctx.SeedPetAsync());
        await ctx.SeedChargeAsync(100m, null, visitId);
        var noCharges = await ctx.NewHttpForAnotherUserAsync([PermissionCatalog.Visits.Read]);

        var today = await noCharges.GetFromJsonAsync<TodayDto>($"/api/v1/visits/today?clinicId={ctx.ClinicId}");

        var item = today!.Items.Single(i => i.VisitId == visitId);
        (item.ChargedAmount, item.AllocatedAmount, item.OutstandingAmount, item.ClientOutstandingAmount)
            .Should().Be(((decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null));
    }

    // ---- Kurulum ----

    private async Task<BalanceTestContext> SeedAsync()
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
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Bal-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            (userId, email) = await BalanceTestContext.SeedUserAsync(db, hasher, tenant.Id, clinic.Id);
            tenantId = tenant.Id;
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, AdminPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ctx = new BalanceTestContext(_factory.Services, hasher, _factory.CreateClient, http, tenantId, clinicId, userId);
        ctx.ClientId = await ctx.SeedClientAsync();
        return ctx;
    }

    private sealed class BalanceTestContext
    {
        private readonly IServiceProvider _services;
        private readonly IPasswordHasher _hasher;
        private readonly Func<HttpClient> _createClient;

        public BalanceTestContext(
            IServiceProvider services, IPasswordHasher hasher, Func<HttpClient> createClient, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
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
        public Guid ClientId { get; set; }

        public static async Task<(Guid UserId, string Email)> SeedUserAsync(
            AppDbContext db, IPasswordHasher hasher, Guid tenantId, Guid clinicId)
        {
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            var email = $"bal-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenantId));
            db.UserClinics.Add(new UserClinic(user.Id, clinicId));
            await db.SaveChangesAsync();
            return (user.Id, email);
        }

        public async Task<HttpClient> NewHttpForAnotherUserAsync(string[] permissions)
        {
            string email;
            await using (var scope = _services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (_, email) = await SeedUserAsync(db, _hasher, TenantId, ClinicId);
            }

            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_services, email, permissions);
            var http = _createClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        public async Task<ClientBalanceDto> GetBalanceAsync()
            => (await Http.GetFromJsonAsync<ClientBalanceDto>($"/api/v1/clients/{ClientId}/balance"))!;

        public async Task<TodayDto> GetTodayAsync()
            => (await Http.GetFromJsonAsync<TodayDto>($"/api/v1/visits/today?clinicId={ClinicId}"))!;

        public Task<HttpResponseMessage> PayAsync(decimal amount, Guid chargeId)
            => Http.PostAsJsonAsync("/api/v1/payments", new
            {
                ClinicId,
                ClientId,
                Amount = amount,
                Currency = "TRY",
                Method = PaymentMethod.Cash,
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-1),
                RequestId = Guid.NewGuid(),
                Allocations = new[] { new { ChargeId = chargeId, Amount = amount } },
            });

        public async Task<decimal> SumAllocationRowsAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var chargeIds = await db.Charges.AsNoTracking()
                .Where(c => c.TenantId == TenantId && c.ClientId == ClientId)
                .Select(c => c.Id)
                .ToListAsync();
            return await db.PaymentAllocations.AsNoTracking()
                .Where(a => a.TenantId == TenantId && chargeIds.Contains(a.ChargeId))
                .SumAsync(a => a.Amount);
        }

        public async Task<Guid> SeedClientAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"BClient-{Guid.NewGuid():N}"[..14], "905551110055");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedPetAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, ClientId, $"BPet-{Guid.NewGuid():N}"[..12], speciesId);
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

        public async Task<Guid> SeedChargeAsync(decimal amount, Guid? petId, Guid? visitId, Guid? clinicId = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = Charge.Create(
                    TenantId, clinicId ?? ClinicId, ClientId, petId, visitId, null,
                    "Seed ucret", 1m, amount, UserId, DateTime.UtcNow, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }

        public async Task SeedLegacyPaymentAsync(decimal amount, Guid? clinicId = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Payments.Add(new Payment(
                TenantId, clinicId ?? ClinicId, ClientId, null, null, null, amount, "TRY", PaymentMethod.Cash,
                DateTime.UtcNow.AddMinutes(-5), "Eski kayit"));
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedForeignClinicAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            return clinic.Id;
        }

        public async Task<Guid> SeedForeignTenantClientAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Foreign-{Guid.NewGuid():N}"[..16]);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            var client = new Client(tenant.Id, $"FClient-{Guid.NewGuid():N}"[..14], "905551110099");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }
    }
}
