using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.SalesPartners;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Application.Tenants.Billing;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class SubscriptionCheckoutActivationServiceTests
{
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptionsRead = new();
    private readonly Mock<IRepository<TenantSubscription>> _subscriptionsWrite = new();
    private readonly Mock<IReadRepository<BillingCheckoutSession>> _sessionsRead = new();
    private readonly Mock<IRepository<BillingCheckoutSession>> _sessionsWrite = new();

    private readonly Mock<IRepository<DiscountRedemption>> _redemptions = new();

    private SubscriptionCheckoutActivationService CreateService()
        => new(
            _subscriptionsRead.Object,
            _subscriptionsWrite.Object,
            _sessionsRead.Object,
            _sessionsWrite.Object,
            Mock.Of<ITenantScope>(),
            new SalesPartnerDiscountBilling(_redemptions.Object, NullLogger<SalesPartnerDiscountBilling>.Instance),
            TimeProvider.System);

    [Fact]
    public async Task TryActivateAsync_Should_ReturnFailure_When_SessionNotFound()
    {
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingCheckoutSession?)null);

        var svc = CreateService();
        var result = await svc.TryActivateAsync(
            Guid.NewGuid(),
            tenantIdConstraint: null,
            providerMustMatch: BillingProvider.Stripe,
            externalReference: null,
            source: BillingActivationSource.Webhook,
            ct: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.CheckoutSessionNotFound");
    }

    [Fact]
    public async Task TryActivateAsync_Should_ReturnFailure_When_ProviderMismatch()
    {
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(),
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Iyzico,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(20));
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var svc = CreateService();
        var result = await svc.TryActivateAsync(
            session.Id,
            tenantIdConstraint: null,
            providerMustMatch: BillingProvider.Stripe,
            externalReference: "pay",
            source: BillingActivationSource.Webhook,
            ct: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Billing.ProviderMismatch");
    }

    [Fact]
    public async Task TryActivateAsync_Should_ReturnFailure_When_TenantConstraintMismatch()
    {
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(),
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Manual,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(20));
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var svc = CreateService();
        var result = await svc.TryActivateAsync(
            session.Id,
            tenantIdConstraint: Guid.NewGuid(),
            providerMustMatch: null,
            externalReference: null,
            source: BillingActivationSource.Manual,
            ct: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task TryActivateAsync_Should_ReturnFailure_When_SessionNotOpen()
    {
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(),
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Manual,
            DateTime.UtcNow.AddHours(-3),
            DateTime.UtcNow.AddHours(-2));
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var svc = CreateService();
        var result = await svc.TryActivateAsync(
            session.Id,
            tenantIdConstraint: session.TenantId,
            providerMustMatch: null,
            externalReference: null,
            source: BillingActivationSource.Manual,
            ct: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.CheckoutSessionNotOpen");
        _sessionsWrite.Verify(x => x.UpdateAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryActivateAsync_Should_ReturnSuccess_When_OpenSessionAndSubscriptionExists()
    {
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(),
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Manual,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30));
        var sub = TenantSubscription.StartTrial(session.TenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _sessionsWrite.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var svc = CreateService();
        var result = await svc.TryActivateAsync(
            session.Id,
            tenantIdConstraint: session.TenantId,
            providerMustMatch: null,
            externalReference: "manual_ref",
            source: BillingActivationSource.Manual,
            ct: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(BillingCheckoutSessionStatus.Completed);
        sub.PlanCode.Should().Be(SubscriptionPlanCode.Pro);
        sub.Status.Should().Be(TenantSubscriptionStatus.Active);
        _subscriptionsWrite.Verify(x => x.UpdateAsync(sub, It.IsAny<CancellationToken>()), Times.Once);
        _sessionsWrite.Verify(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryActivateAsync_Should_RefreshActivatedAtUtc_When_RenewingExpiredActiveSubscription()
    {
        var tenantId = Guid.NewGuid();
        var session = BillingCheckoutSession.CreatePending(
            tenantId,
            SubscriptionPlanCode.Premium,
            SubscriptionPlanCode.Premium,
            BillingProvider.Manual,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30));

        // Süresi dolmuş (renewal öncesi) aktif ücretli abonelik: persiste edilen Status "Active" ama
        // ActivatedAtUtc.AddMonths(1) geçmişte kaldığı için effective status ReadOnly'dir.
        var oldActivatedAt = DateTime.UtcNow.AddDays(-46);
        var sub = TenantSubscription.StartTrial(tenantId, SubscriptionPlanCode.Premium, oldActivatedAt.AddDays(-14), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Premium, oldActivatedAt);

        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _sessionsWrite.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var svc = CreateService();
        var utcNow = DateTime.UtcNow;
        var result = await svc.TryActivateAsync(
            session.Id,
            tenantIdConstraint: tenantId,
            providerMustMatch: null,
            externalReference: "manual_renewal_ref",
            source: BillingActivationSource.Manual,
            ct: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(TenantSubscriptionStatus.Active);
        sub.ActivatedAtUtc.Should().BeCloseTo(utcNow, TimeSpan.FromSeconds(5));

        var effectiveAfterRenewal = Backend.Veteriner.Application.Tenants.TenantSubscriptionEffectiveWriteEvaluator
            .GetEffectiveStatus(sub, utcNow);
        effectiveAfterRenewal.Should().Be(TenantSubscriptionStatus.Active);
    }

    [Theory]
    [InlineData(BillingProvider.Iyzico)]
    [InlineData(BillingProvider.Stripe)]
    public async Task TryActivateAsync_Should_Reject_Manual_Finalize_For_Provider_Verified_Sessions(BillingProvider provider)
    {
        // BILL-002: panel finalize sağlayıcıdan doğrulanmamış ödemeyle aboneliği etkinleştiremez.
        var tenantId = Guid.NewGuid();
        var session = BillingCheckoutSession.CreatePending(
            tenantId, SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, provider, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));
        var sub = TenantSubscription.StartTrial(tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-20), 14);
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);

        var result = await CreateService().TryActivateAsync(
            session.Id, tenantId, null, "fake-ref", BillingActivationSource.Manual, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Billing.ManualFinalizeNotAllowed");
        session.Status.Should().NotBe(BillingCheckoutSessionStatus.Completed);
        sub.PlanCode.Should().Be(SubscriptionPlanCode.Basic);
        sub.Status.Should().Be(TenantSubscriptionStatus.Trialing);
        _subscriptionsWrite.Verify(x => x.UpdateAsync(It.IsAny<TenantSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
        _sessionsWrite.Verify(x => x.UpdateAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(BillingActivationSource.Webhook, BillingProvider.Iyzico, 1)]
    [InlineData(BillingActivationSource.Manual, BillingProvider.Manual, 0)]
    public async Task TryActivateAsync_Should_Record_Partner_Commission_Only_For_Verified_Webhook_Collection(
        BillingActivationSource source, BillingProvider provider, int expectedCollections)
    {
        var tenantId = Guid.NewGuid();
        var session = BillingCheckoutSession.CreatePending(
            tenantId, SubscriptionPlanCode.Basic, SubscriptionPlanCode.Basic, provider, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));
        session.RecordPricing(99_900, 9_990);
        var sub = TenantSubscription.StartTrial(tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-20), 14);

        var partner = SalesPartner.Create("Ortak", null, null, null, DateTime.UtcNow);
        var code = DiscountCode.Create(partner.Id, "ISIK10", 10m, 20m, null, null, null, DateTime.UtcNow).Value!;
        var redemption = DiscountRedemption.Redeem(tenantId, null, code, partner, 0, DateTime.UtcNow).Value!;
        _redemptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<DiscountRedemptionByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(redemption);
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);

        var result = await CreateService().TryActivateAsync(session.Id, null, null, "pay-1", source, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        redemption.Collections.Should().HaveCount(expectedCollections);
        if (expectedCollections == 1)
        {
            var collection = redemption.Collections.Single();
            collection.CheckoutSessionId.Should().Be(session.Id);
            collection.NetAmountMinor.Should().Be(89_910);
            collection.CommissionAmountMinor.Should().Be(17_982);
            redemption.WindowStartsAtUtc.Should().NotBeNull();
        }
    }
}
