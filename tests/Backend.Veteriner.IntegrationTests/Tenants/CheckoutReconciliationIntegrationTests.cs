using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Tenants.Billing;
using Backend.Veteriner.Application.Tenants.Commands.Billing;
using Backend.Veteriner.Application.Tenants.Queries.GetDuplicatePayments;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Billing;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Mediator;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// Webhook kaybolup kullanıcı da dönmediğinde ödeme boşa gitmesin (HTTP + LocalDB): sağlayıcıda ödenmiş ama bizde
/// iptal edilmiş oturum, periyodik uzlaştırmanın tek turunda etkinleşir. Sağlayıcı sorgusu sahte (iyzico'ya gidilmez).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class CheckoutReconciliationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CheckoutReconciliationIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Reconciliation_Should_Activate_Paid_But_Cancelled_Iyzico_Session()
    {
        await using var app = _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IBillingCheckoutProvider>();
            services.AddScoped<IBillingCheckoutProvider, ManualBillingCheckoutProvider>();
            services.AddScoped<IBillingCheckoutProvider, PaidIyzicoProvider>();
        }));
        var tenantId = await SignupOwnerAsync(app);
        var sessionId = await SeedCancelledIyzicoSessionAsync(app, tenantId);

        int paid;
        await using (var scope = app.Services.CreateAsyncScope())
            paid = await scope.ServiceProvider.GetRequiredService<CheckoutReconciliationProcessor>().RunOnceAsync(CancellationToken.None);

        paid.Should().Be(1);
        await using var check = app.Services.CreateSystemScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.BillingCheckoutSessions.SingleAsync(s => s.Id == sessionId)).Status.Should().Be(BillingCheckoutSessionStatus.Completed);
        var sub = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        sub.PlanCode.Should().Be(SubscriptionPlanCode.Pro);
        sub.Status.Should().Be(TenantSubscriptionStatus.Active);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reconciliation_Should_Refund_Duplicate_Payment_Instead_Of_Changing_Plan(bool refundSucceeds)
    {
        // BILL-005: paket değiştirildi, yeni oturum ödenip Premium açıldı; eski (iptal) sayfa da ödendi. Eski ödeme paketi
        // değiştirmez, iade edilir; iade reddedilirse yönetici listesinde "elle iade" görünür ve işaretlenebilir.
        await using var app = _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IBillingCheckoutProvider>();
            services.AddScoped<IBillingCheckoutProvider, ManualBillingCheckoutProvider>();
            services.AddSingleton<IBillingCheckoutProvider>(new PaidIyzicoProvider(refundSucceeds));
        }));
        var tenantId = await SignupOwnerAsync(app);
        var duplicateId = await SeedCancelledIyzicoSessionAsync(app, tenantId);
        await SeedSettledPremiumSessionAsync(app, tenantId);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<CheckoutReconciliationProcessor>();
            await processor.RunOnceAsync(CancellationToken.None);
            for (var i = 0; i < DuplicatePaymentRefunder.MaxAutomaticAttempts; i++)
                await processor.RefundPendingOnceAsync(CancellationToken.None);
        }

        await using var check = app.Services.CreateSystemScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var duplicate = await db.BillingCheckoutSessions.AsNoTracking().SingleAsync(s => s.Id == duplicateId);
        duplicate.Status.Should().Be(BillingCheckoutSessionStatus.Cancelled);
        duplicate.RefundStatus.Should().Be(refundSucceeds ? BillingCheckoutRefundStatus.Refunded : BillingCheckoutRefundStatus.ManualRequired);
        (await db.TenantSubscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId)).PlanCode.Should().Be(SubscriptionPlanCode.Premium);

        var mediator = check.ServiceProvider.GetRequiredService<IMediator>();
        var listed = (await mediator.Send(new GetDuplicatePaymentsQuery())).Single(x => x.CheckoutSessionId == duplicateId);
        listed.RefundStatus.Should().Be(refundSucceeds ? "Refunded" : "ManualRequired");
        if (refundSucceeds)
            return;

        listed.RefundFailureReason.Should().Be("iade reddedildi");
        (await mediator.Send(new MarkDuplicatePaymentRefundedCommand(duplicateId))).IsSuccess.Should().BeTrue();
        (await db.BillingCheckoutSessions.AsNoTracking().SingleAsync(s => s.Id == duplicateId)).RefundStatus
            .Should().Be(BillingCheckoutRefundStatus.Refunded);
    }

    private static async Task SeedSettledPremiumSessionAsync(WebApplicationFactory<Program> app, Guid tenantId)
    {
        await using var scope = app.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var session = BillingCheckoutSession.CreatePending(
            tenantId, SubscriptionPlanCode.Basic, SubscriptionPlanCode.Premium, BillingProvider.Iyzico, now.AddMinutes(-15), now.AddMinutes(15));
        session.SetRedirectReady("https://sandbox.example/checkout?token=settled-token", "settled-token", now.AddMinutes(-15));
        session.MarkCompleted(now.AddMinutes(-5));
        db.BillingCheckoutSessions.Add(session);
        var sub = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Premium, BillingPeriodKind.Monthly, now.AddMinutes(-5));
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedCancelledIyzicoSessionAsync(WebApplicationFactory<Program> app, Guid tenantId)
    {
        await using var scope = app.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var session = BillingCheckoutSession.CreatePending(
            tenantId, SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, BillingProvider.Iyzico, now.AddMinutes(-20), now.AddMinutes(10));
        session.SetRedirectReady("https://sandbox.example/checkout?token=paid-token", "paid-token", now.AddMinutes(-20));
        session.MarkCancelled(now.AddMinutes(-15));
        db.BillingCheckoutSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static async Task<Guid> SignupOwnerAsync(WebApplicationFactory<Program> app)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

        var signup = await app.CreateClient().PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Uzlaştırma Kiracı {suffix}",
            clinicName = "Uzlaştırma Klinik",
            clinicCity = "İzmir",
            email = $"uzlastirma-{suffix}@example.com",
            password = "Abc12345!",
            acceptedLegalDocuments = documents,
        });
        signup.StatusCode.Should().Be(HttpStatusCode.OK, await signup.Content.ReadAsStringAsync());
        return (await signup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
    }

    /// <summary>Sağlayıcı sorgusu "ödeme alındı" der; ödeme sayfası hazırlamaz. İade isteği verilen sonucu döner.</summary>
    private sealed class PaidIyzicoProvider(bool refundSucceeds = true) : IBillingCheckoutProvider
    {
        public BillingProvider Provider => BillingProvider.Iyzico;

        public Task<ProviderRefundResult> RefundPaymentAsync(BillingCheckoutSession session, CancellationToken ct = default)
            => Task.FromResult(refundSucceeds ? ProviderRefundResult.Success() : ProviderRefundResult.Failed("iade reddedildi"));

        public Task<Result<CheckoutPrepareResult>> PrepareCheckoutAsync(
            BillingCheckoutSession session, string? chargeCurrencyCode, long? chargeAmountMinor, decimal? prorationRatio, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<CheckoutPageState> GetCheckoutPageStateAsync(BillingCheckoutSession session, CancellationToken ct = default)
            => Task.FromResult(CheckoutPageState.Paid);
    }
}
