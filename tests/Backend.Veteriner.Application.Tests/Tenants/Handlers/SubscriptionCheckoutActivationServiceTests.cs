using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Billing;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class SubscriptionCheckoutActivationServiceTests
{
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptionsRead = new();
    private readonly Mock<IRepository<TenantSubscription>> _subscriptionsWrite = new();
    private readonly Mock<IReadRepository<BillingCheckoutSession>> _sessionsRead = new();
    private readonly Mock<IRepository<BillingCheckoutSession>> _sessionsWrite = new();

    private SubscriptionCheckoutActivationService CreateService()
        => new(_subscriptionsRead.Object, _subscriptionsWrite.Object, _sessionsRead.Object, _sessionsWrite.Object);

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
}
