using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Payments;

/// <summary>
/// Ücret düzeltme, ücret iptali ve ödeme iptali uçtan uca (HTTP + izole LocalDB test veritabanı):
/// tahsislerin geri alınması, bakiyenin yeniden hesaplanması, iptal edilenin okuma yüzeylerinden düşmesi,
/// sayaç değişmezi, requestId tüketimi, yetki, gerekçe ve audit.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class FinanceCorrectionIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Reason = "Yanlis kayit girildi";

    private static readonly string[] AdminPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Payments.Update,
        PermissionCatalog.Payments.Correct,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
        PermissionCatalog.Charges.Correct,
    ];

    private static readonly string[] ReceptionPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Payments.Update,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public FinanceCorrectionIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Voiding_Second_Payment_Of_1000_Charge_Restores_600_Outstanding_And_Hides_The_Payment()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(1000m);
        await ctx.PostPaymentAsync(chargeId, 400m);
        var second = await ctx.PostPaymentAsync(chargeId, 600m);
        (await ctx.GetChargeAsync(chargeId)).OutstandingAmount.Should().Be(0m);

        var response = await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{second}/void", new { Reason });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var charge = await ctx.GetChargeAsync(chargeId);
        charge.AllocatedAmount.Should().Be(400m);
        charge.OutstandingAmount.Should().Be(600m);

        (await ctx.Http.GetAsync($"/api/v1/payments/{second}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.GetAsync($"/api/v1/payments/{second}/allocations")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.GetFromJsonAsync<List<ChargeAllocationItemDto>>($"/api/v1/charges/{chargeId}/allocations"))!
            .Select(a => a.Amount).Should().BeEquivalentTo(new[] { 400m });

        var balance = await ctx.GetBalanceAsync();
        balance.ChargedAmount.Should().Be(1000m);
        balance.AllocatedAmount.Should().Be(400m);
        balance.OutstandingAmount.Should().Be(600m);

        await ctx.AssertActiveAllocationsMatchCountersAsync();
        (await ctx.CountReversedAllocationsAsync(second)).Should().Be(1);
    }

    [Fact]
    public async Task Voiding_Payment_Twice_Returns_409_AlreadyVoided_And_Is_Audited_With_Reason()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(500m);
        var paymentId = await ctx.PostPaymentAsync(chargeId, 500m);

        (await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{paymentId}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var again = await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{paymentId}/void", new { Reason });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("Payments.AlreadyVoided");
        (await ctx.GetChargeAsync(chargeId)).OutstandingAmount.Should().Be(500m);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Payment.Void" && a.TargetId == $"PaymentId={paymentId}")
            .ToListAsync();
        logs.Should().Contain(l => l.Success && l.ActorUserId == ctx.UserId && l.RequestPayload!.Contains(Reason));
        logs.Should().Contain(l => !l.Success);

        var row = await db.Payments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == paymentId);
        row.VoidedByUserId.Should().Be(ctx.UserId);
        row.VoidReason.Should().Be(Reason);
        row.AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Legacy_Unallocated_Payment_Can_Be_Voided_And_Drops_From_List()
    {
        var ctx = await SeedAsync();
        var legacy = await ctx.SeedLegacyPaymentAsync(120m);

        var response = await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{legacy}/void", new { Reason });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.Http.GetAsync($"/api/v1/payments/{legacy}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var balance = await ctx.GetBalanceAsync();
        balance.UnallocatedPayments.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Void_Needs_Reason_And_Correct_Permission_Else_400_And_403()
    {
        var admin = await SeedAsync();
        var reception = await SeedAsync(ReceptionPermissions);
        var adminCharge = await admin.SeedChargeAsync(300m);
        var adminPayment = await admin.PostPaymentAsync(adminCharge, 300m);
        var receptionCharge = await reception.SeedChargeAsync(300m);
        var receptionPayment = await reception.PostPaymentAsync(receptionCharge, 300m);

        var noReason = await admin.Http.PostAsJsonAsync($"/api/v1/payments/{adminPayment}/void", new { Reason = "abc" });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noReason)).Should().Be("Payments.Validation");

        (await reception.Http.PostAsJsonAsync($"/api/v1/payments/{receptionPayment}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reception.Http.PostAsJsonAsync($"/api/v1/charges/{receptionCharge}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reception.Http.PutAsJsonAsync($"/api/v1/charges/{receptionCharge}", new
            {
                Description = "Yeni", Quantity = 1m, UnitPrice = 250m, Reason,
            }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await reception.GetChargeAsync(receptionCharge)).AllocatedAmount.Should().Be(300m);
    }

    [Fact]
    public async Task Paid_Charge_Cannot_Be_Voided_Until_Its_Payment_Is_Voided()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(800m);
        var paymentId = await ctx.PostPaymentAsync(chargeId, 200m);

        var blocked = await ctx.Http.PostAsJsonAsync($"/api/v1/charges/{chargeId}/void", new { Reason });
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(blocked)).Should().Be("Charges.VoidBlockedByAllocation");
        (await ctx.GetChargeAsync(chargeId)).AllocatedAmount.Should().Be(200m);

        (await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{paymentId}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.Http.PostAsJsonAsync($"/api/v1/charges/{chargeId}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ctx.Http.GetAsync($"/api/v1/charges/{chargeId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var balance = await ctx.GetBalanceAsync();
        balance.ChargedAmount.Should().Be(0m);
        balance.OutstandingAmount.Should().Be(0m);

        var again = await ctx.Http.PostAsJsonAsync($"/api/v1/charges/{chargeId}/void", new { Reason });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("Charges.AlreadyVoided");
    }

    [Fact]
    public async Task Charge_Correction_Recalculates_Amount_And_Cannot_Go_Below_Allocated()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(1000m);
        await ctx.PostPaymentAsync(chargeId, 400m);

        var below = await ctx.Http.PutAsJsonAsync($"/api/v1/charges/{chargeId}", new
        {
            Description = "Indirimli muayene", Quantity = 1m, UnitPrice = 399m, Reason,
        });
        below.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(below)).Should().Be("Charges.CorrectionBelowAllocated");

        var noReason = await ctx.Http.PutAsJsonAsync($"/api/v1/charges/{chargeId}", new
        {
            Description = "Indirimli muayene", Quantity = 1m, UnitPrice = 700m,
        });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noReason)).Should().Be("Charges.Validation");

        var ok = await ctx.Http.PutAsJsonAsync($"/api/v1/charges/{chargeId}", new
        {
            Description = "Indirimli muayene", Quantity = 1m, UnitPrice = 700m, Reason,
        });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await ok.Content.ReadFromJsonAsync<ChargeDto>())!;
        dto.Amount.Should().Be(700m);
        dto.OutstandingAmount.Should().Be(300m);
        dto.Description.Should().Be("Indirimli muayene");

        var balance = await ctx.GetBalanceAsync();
        balance.ChargedAmount.Should().Be(700m);
        balance.OutstandingAmount.Should().Be(300m);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.Action == "Charge.Correct" && a.TargetId == $"ChargeId={chargeId}" && a.Success
            && a.ActorUserId == ctx.UserId && a.RequestPayload!.Contains(Reason))).Should().BeTrue();
    }

    [Fact]
    public async Task RequestId_Of_A_Voided_Payment_Or_Charge_Is_Consumed_With_409()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(300m);
        var requestId = Guid.NewGuid();
        var paidAt = DateTime.UtcNow.AddMinutes(-2);
        var paymentId = await ctx.PostPaymentAsync(chargeId, 300m, requestId, paidAt);
        await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{paymentId}/void", new { Reason });

        var replay = await ctx.PostPaymentRawAsync(chargeId, 300m, requestId, paidAt);

        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(replay)).Should().Be("Payments.RequestIdVoided");

        var chargeRequestId = Guid.NewGuid();
        var created = await ctx.Http.PostAsJsonAsync("/api/v1/charges", ctx.ChargeBody(chargeRequestId));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var newChargeId = (await created.Content.ReadFromJsonAsync<ChargeCreateResultDto>())!.Charge.Id;
        (await ctx.Http.PostAsJsonAsync($"/api/v1/charges/{newChargeId}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var chargeReplay = await ctx.Http.PostAsJsonAsync("/api/v1/charges", ctx.ChargeBody(chargeRequestId));

        chargeReplay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(chargeReplay)).Should().Be("Charges.RequestIdVoided");
    }

    [Fact]
    public async Task Void_Of_Unknown_Or_Foreign_Record_Returns_404()
    {
        var ctx = await SeedAsync();

        (await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{Guid.NewGuid()}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.PostAsJsonAsync($"/api/v1/charges/{Guid.NewGuid()}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.PutAsJsonAsync($"/api/v1/charges/{Guid.NewGuid()}", new
            {
                Description = "Yok", Quantity = 1m, UnitPrice = 10m, Reason,
            }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Kurulum ----

    private async Task<Ctx> SeedAsync(string[]? permissions = null)
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
            var clinic = new Clinic(tenant.Id, $"Fin-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"fin-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
            userId = user.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, permissions ?? AdminPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ctx = new Ctx(_factory.Services, http, tenantId, clinicId, userId);
        ctx.ClientId = await ctx.SeedClientAsync();
        return ctx;
    }

    private sealed class Ctx
    {
        private readonly IServiceProvider _services;

        public Ctx(IServiceProvider services, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
        {
            _services = services;
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

        public object ChargeBody(Guid requestId)
            => new
            {
                ClinicId,
                ClientId,
                Description = "Muayene",
                Quantity = 1m,
                UnitPrice = 100m,
                RequestId = requestId,
            };

        public Task<HttpResponseMessage> PostPaymentRawAsync(Guid chargeId, decimal amount, Guid? requestId = null, DateTime? paidAtUtc = null)
            => Http.PostAsJsonAsync("/api/v1/payments", new
            {
                ClinicId,
                ClientId,
                Amount = amount,
                Currency = "TRY",
                Method = PaymentMethod.Cash,
                PaidAtUtc = paidAtUtc ?? DateTime.UtcNow.AddMinutes(-1),
                RequestId = requestId ?? Guid.NewGuid(),
                Allocations = new[] { new { ChargeId = chargeId, Amount = amount } },
            });

        public async Task<Guid> PostPaymentAsync(Guid chargeId, decimal amount, Guid? requestId = null, DateTime? paidAtUtc = null)
        {
            var response = await PostPaymentRawAsync(chargeId, amount, requestId, paidAtUtc);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return await response.Content.ReadFromJsonAsync<Guid>();
        }

        public async Task<ChargeDto> GetChargeAsync(Guid chargeId)
            => (await Http.GetFromJsonAsync<ChargeDto>($"/api/v1/charges/{chargeId}"))!;

        public async Task<ClientBalanceDto> GetBalanceAsync()
            => (await Http.GetFromJsonAsync<ClientBalanceDto>($"/api/v1/clients/{ClientId}/balance"))!;

        public async Task<Guid> SeedClientAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"FClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110055");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedChargeAsync(decimal amount)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = Charge.Create(
                    TenantId, ClinicId, ClientId, null, null, null,
                    "Seed ucret", 1m, amount, UserId, DateTime.UtcNow, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }

        public async Task<Guid> SeedLegacyPaymentAsync(decimal amount)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payment = new Payment(
                TenantId, ClinicId, ClientId, null, null, null, amount, "TRY", PaymentMethod.Cash,
                DateTime.UtcNow.AddMinutes(-5), "Eski kayit");
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            return payment.Id;
        }

        public async Task<int> CountReversedAllocationsAsync(Guid paymentId)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.PaymentAllocations.IgnoreQueryFilters()
                .CountAsync(a => a.PaymentId == paymentId && a.ReversedAtUtc != null);
        }

        /// <summary>Sayaç değişmezi: Σ aktif PaymentAllocations.Amount == AllocatedAmount (iptal edilenin sayacı 0).</summary>
        public async Task AssertActiveAllocationsMatchCountersAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var activeByCharge = await db.PaymentAllocations.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.ClinicId == ClinicId)
                .GroupBy(a => a.ChargeId)
                .Select(g => new { Id = g.Key, Sum = g.Sum(x => x.Amount) })
                .ToListAsync();
            foreach (var row in activeByCharge)
            {
                (await db.Charges.AsNoTracking().SingleAsync(c => c.Id == row.Id)).AllocatedAmount.Should().Be(row.Sum);
            }

            var activeByPayment = await db.PaymentAllocations.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.ClinicId == ClinicId)
                .GroupBy(a => a.PaymentId)
                .Select(g => new { Id = g.Key, Sum = g.Sum(x => x.Amount) })
                .ToListAsync();
            foreach (var row in activeByPayment)
            {
                (await db.Payments.AsNoTracking().SingleAsync(p => p.Id == row.Id)).AllocatedAmount.Should().Be(row.Sum);
            }

            (await db.Payments.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.TenantId == TenantId && p.ClinicId == ClinicId && p.VoidedAtUtc != null)
                .AllAsync(p => p.AllocatedAmount == 0m)).Should().BeTrue();
        }
    }
}
