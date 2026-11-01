using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.ClientCredits;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
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
/// Müşteri kredisi ve mahsup uçtan uca (FIN-005; HTTP + izole LocalDB): fazla ödeme/avans → kredi, mahsup → açık tutar,
/// bakiye, sayaç değişmezi, geri alma, iade → gün sonu dökümü ve kasa, ödeme iptaliyle etkileşim, yetki ve kapsam.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClientCreditIntegrationTests : IClassFixture<CustomWebApplicationFactory>
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
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public ClientCreditIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Overpayment_Becomes_Credit_And_Is_Applied_To_Next_Charge()
    {
        var ctx = await SeedAsync();
        var first = await ctx.SeedChargeAsync(600m);

        var paymentId = await ctx.PostPaymentAsync(1000m, [(first, 600m)], creditAmount: 400m);

        var balance = await ctx.GetBalanceAsync();
        balance.OutstandingAmount.Should().Be(0m);
        balance.CreditBalance.Should().Be(400m);
        balance.OpenCreditCount.Should().Be(1);
        balance.UnallocatedPayments.TotalAmount.Should().Be(0m);

        var detail = (await ctx.Http.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{paymentId}"))!;
        detail.CreditedAmount.Should().Be(400m);
        detail.UnallocatedAmount.Should().Be(0m);

        var second = await ctx.SeedChargeAsync(250m);
        var apply = await ctx.ApplyAsync([(second, 250m)]);

        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await apply.Content.ReadFromJsonAsync<ClientCreditApplyResultDto>())!;
        result.AppliedAmount.Should().Be(250m);
        result.RemainingCreditBalance.Should().Be(150m);

        (await ctx.GetChargeAsync(second)).OutstandingAmount.Should().Be(0m);
        balance = await ctx.GetBalanceAsync();
        balance.OutstandingAmount.Should().Be(0m);
        balance.CreditBalance.Should().Be(150m);

        var allocations = (await ctx.Http.GetFromJsonAsync<List<ChargeAllocationItemDto>>($"/api/v1/charges/{second}/allocations"))!;
        allocations.Should().ContainSingle(a => a.Kind == ChargeAllocationKinds.Credit && a.Amount == 250m && a.PaymentId == paymentId);

        var credits = (await ctx.Http.GetFromJsonAsync<List<ClientCreditDto>>($"/api/v1/clients/{ctx.ClientId}/credits"))!;
        credits.Should().ContainSingle();
        credits[0].Source.Should().Be(ClientCreditSource.Overpayment);
        credits[0].RemainingAmount.Should().Be(150m);
        credits[0].Applications.Should().ContainSingle(a => a.ChargeId == second && a.ChargeDescription == "Seed ucret");

        await ctx.AssertCountersMatchRowsAsync();
    }

    [Fact]
    public async Task Prepayment_Without_Allocations_Is_Credit_And_Apply_Replay_Does_Not_Double_Count()
    {
        var ctx = await SeedAsync(ReceptionPermissions);
        await ctx.PostPaymentAsync(300m, [], creditAmount: 300m);
        var charge = await ctx.SeedChargeAsync(200m);
        var requestId = Guid.NewGuid();

        (await ctx.ApplyAsync([(charge, 200m)], requestId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await ctx.ApplyAsync([(charge, 200m)], requestId);

        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<ClientCreditApplyResultDto>())!.RemainingCreditBalance.Should().Be(100m);
        (await ctx.GetBalanceAsync()).CreditBalance.Should().Be(100m);
        (await ctx.GetChargeAsync(charge)).AllocatedAmount.Should().Be(200m);
        await ctx.AssertCountersMatchRowsAsync();
    }

    [Fact]
    public async Task Apply_More_Than_Credit_Returns_409_And_Sum_Mismatch_Returns_400()
    {
        var ctx = await SeedAsync();
        await ctx.PostPaymentAsync(100m, [], creditAmount: 100m);
        var charge = await ctx.SeedChargeAsync(500m);

        var tooMuch = await ctx.ApplyAsync([(charge, 150m)]);
        tooMuch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(tooMuch)).Should().Be("ClientCredits.InsufficientBalance");

        var mismatch = await ctx.PostPaymentRawAsync(500m, [(charge, 300m)], creditAmount: 100m);
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(mismatch)).Should().Be("Payments.AllocationSumMismatch");

        (await ctx.GetChargeAsync(charge)).AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Void_Is_Blocked_While_Credit_Is_Used_And_Voids_Credit_After_Reversal()
    {
        var ctx = await SeedAsync();
        var paymentId = await ctx.PostPaymentAsync(300m, [], creditAmount: 300m);
        var charge = await ctx.SeedChargeAsync(120m);
        var applied = (await (await ctx.ApplyAsync([(charge, 120m)])).Content.ReadFromJsonAsync<ClientCreditApplyResultDto>())!;

        var blocked = await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{paymentId}/void", new { Reason });
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(blocked)).Should().Be("Payments.VoidBlockedByCredit");

        var reverse = await ctx.Http.PostAsJsonAsync(
            $"/api/v1/clients/{ctx.ClientId}/credits/applications/{applied.Applications[0].Id}/reverse", new { Reason });
        reverse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.GetChargeAsync(charge)).OutstandingAmount.Should().Be(120m);

        (await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{paymentId}/void", new { Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var balance = await ctx.GetBalanceAsync();
        balance.CreditBalance.Should().Be(0m);
        var all = (await ctx.Http.GetFromJsonAsync<List<ClientCreditDto>>($"/api/v1/clients/{ctx.ClientId}/credits?includeClosed=true"))!;
        all.Should().ContainSingle(c => c.Status == ClientCreditStatus.Voided);
        await ctx.AssertCountersMatchRowsAsync();
    }

    [Fact]
    public async Task Refund_Needs_Correct_Permission_And_Appears_As_Cash_Outflow()
    {
        var reception = await SeedAsync(ReceptionPermissions);
        await reception.PostPaymentAsync(200m, [], creditAmount: 200m);
        var receptionCredit = (await reception.Http.GetFromJsonAsync<List<ClientCreditDto>>($"/api/v1/clients/{reception.ClientId}/credits"))![0];
        (await reception.Http.PostAsJsonAsync(
                $"/api/v1/clients/{reception.ClientId}/credits/{receptionCredit.Id}/refund", new { Reason, Method = "Cash" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ctx = await SeedAsync();
        await ctx.PostPaymentAsync(500m, [], creditAmount: 500m);
        var charge = await ctx.SeedChargeAsync(200m);
        (await ctx.ApplyAsync([(charge, 200m)])).StatusCode.Should().Be(HttpStatusCode.OK);
        var credit = (await ctx.Http.GetFromJsonAsync<List<ClientCreditDto>>($"/api/v1/clients/{ctx.ClientId}/credits"))![0];

        var refund = await ctx.Http.PostAsJsonAsync(
            $"/api/v1/clients/{ctx.ClientId}/credits/{credit.Id}/refund", new { Reason, Method = "Cash" });

        refund.StatusCode.Should().Be(HttpStatusCode.OK);
        var refunded = (await refund.Content.ReadFromJsonAsync<ClientCreditDto>())!;
        refunded.Status.Should().Be(ClientCreditStatus.Refunded);
        refunded.RefundedAmount.Should().Be(300m);
        (await ctx.GetBalanceAsync()).CreditBalance.Should().Be(0m);

        var again = await ctx.Http.PostAsJsonAsync(
            $"/api/v1/clients/{ctx.ClientId}/credits/{credit.Id}/refund", new { Reason, Method = "Cash" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("ClientCredits.NothingToRefund");

        var today = OperationDayBounds.ToLocalDate(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var report = (await ctx.Http.GetFromJsonAsync<DailyTransactionsReportResultDto>(
            $"/api/v1/reports/daily-transactions?from={today}&to={today}&clinicId={ctx.ClinicId}&pageSize=200"))!;
        report.Items.Should().ContainSingle(i => i.Kind == DailyTransactionKind.CreditRefund && i.Amount == 300m && i.ReferenceId == credit.Id);
        report.Totals.RefundedCreditAmount.Should().Be(300m);
        report.Totals.NetCollectedAmount.Should().Be(report.Totals.CollectedAmount - report.Totals.VoidedCollectionAmount - 300m);

        var cash = (await ctx.Http.GetFromJsonAsync<CashSummaryReportDto>(
            $"/api/v1/reports/cash-summary?from={today}&to={today}&clinicId={ctx.ClinicId}"))!;
        cash.Totals.CollectedAmount.Should().Be(500m);
        cash.Totals.RefundedCreditAmount.Should().Be(300m);
        cash.Totals.NetAmount.Should().Be(200m);
        cash.ByUser.Should().ContainSingle(u => u.UserId == ctx.UserId && u.NetAmount == 200m);
    }

    [Fact]
    public async Task Credit_Of_Other_Clinic_Is_Not_Visible_Or_Applicable()
    {
        var a = await SeedAsync();
        var b = await SeedAsync();
        await a.PostPaymentAsync(100m, [], creditAmount: 100m);
        var bCharge = await b.SeedChargeAsync(50m);

        var credits = (await b.Http.GetFromJsonAsync<List<ClientCreditDto>>($"/api/v1/clients/{a.ClientId}/credits"))!;
        credits.Should().BeEmpty();

        var cross = await b.Http.PostAsJsonAsync($"/api/v1/clients/{a.ClientId}/credits/apply", new
        {
            ClinicId = b.ClinicId,
            RequestId = Guid.NewGuid(),
            Allocations = new[] { new { ChargeId = bCharge, Amount = 50m } },
        });
        cross.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(cross)).Should().Be("ClientCredits.InsufficientBalance");
    }

    private async Task<Ctx> SeedAsync(string[]? permissions = null)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Crd-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"crd-user-{Guid.NewGuid():N}@example.com";
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

        public Task<HttpResponseMessage> PostPaymentRawAsync(decimal amount, (Guid ChargeId, decimal Amount)[] lines, decimal creditAmount)
            => Http.PostAsJsonAsync("/api/v1/payments", new
            {
                ClinicId,
                ClientId,
                Amount = amount,
                Currency = "TRY",
                Method = PaymentMethod.Cash,
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-1),
                RequestId = Guid.NewGuid(),
                Allocations = lines.Select(l => new { l.ChargeId, l.Amount }).ToArray(),
                CreditAmount = creditAmount,
            });

        public async Task<Guid> PostPaymentAsync(decimal amount, (Guid ChargeId, decimal Amount)[] lines, decimal creditAmount)
        {
            var response = await PostPaymentRawAsync(amount, lines, creditAmount);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Guid>();
        }

        public Task<HttpResponseMessage> ApplyAsync((Guid ChargeId, decimal Amount)[] lines, Guid? requestId = null)
            => Http.PostAsJsonAsync($"/api/v1/clients/{ClientId}/credits/apply", new
            {
                ClinicId,
                RequestId = requestId ?? Guid.NewGuid(),
                Allocations = lines.Select(l => new { l.ChargeId, l.Amount }).ToArray(),
            });

        public async Task<ChargeDto> GetChargeAsync(Guid chargeId)
            => (await Http.GetFromJsonAsync<ChargeDto>($"/api/v1/charges/{chargeId}"))!;

        public async Task<ClientBalanceDto> GetBalanceAsync()
            => (await Http.GetFromJsonAsync<ClientBalanceDto>($"/api/v1/clients/{ClientId}/balance"))!;

        public async Task<Guid> SeedClientAsync()
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"CrClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110066");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedChargeAsync(decimal amount)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = Charge.Create(
                    TenantId, ClinicId, ClientId, null, null, null,
                    "Seed ucret", 1m, amount, UserId, DateTime.UtcNow, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }

        /// <summary>
        /// FIN-005 değişmezi: ücret sayacı = Σ aktif ödeme tahsisi + Σ aktif kredi mahsubu; kredi kullanılan = Σ aktif mahsup.
        /// </summary>
        public async Task AssertCountersMatchRowsAsync()
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var charges = await db.Charges.AsNoTracking()
                .Where(c => c.TenantId == TenantId && c.ClinicId == ClinicId && c.ClientId == ClientId)
                .ToListAsync();
            foreach (var charge in charges)
            {
                var paid = await db.PaymentAllocations.AsNoTracking().Where(a => a.ChargeId == charge.Id).SumAsync(a => a.Amount);
                var credited = await db.ClientCreditApplications.AsNoTracking()
                    .Where(a => a.ChargeId == charge.Id && a.ReversedAtUtc == null).SumAsync(a => a.Amount);
                charge.AllocatedAmount.Should().Be(paid + credited);
            }

            var credits = await db.ClientCredits.AsNoTracking().Where(c => c.TenantId == TenantId && c.ClientId == ClientId).ToListAsync();
            foreach (var credit in credits)
            {
                var used = await db.ClientCreditApplications.AsNoTracking()
                    .Where(a => a.CreditId == credit.Id && a.ReversedAtUtc == null).SumAsync(a => a.Amount);
                credit.UsedAmount.Should().Be(used);
            }
        }
    }
}
