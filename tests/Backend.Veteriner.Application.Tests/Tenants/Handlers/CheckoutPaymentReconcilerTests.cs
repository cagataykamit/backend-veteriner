using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Billing;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class CheckoutPaymentReconcilerTests
{
    private readonly Mock<IBillingCheckoutProviderResolver> _resolver = new();
    private readonly Mock<IBillingCheckoutProvider> _provider = new();
    private readonly Mock<ISubscriptionCheckoutActivationService> _activation = new();

    private CheckoutPaymentReconciler Create() => new(_resolver.Object, _activation.Object, NullLogger<CheckoutPaymentReconciler>.Instance);

    private BillingCheckoutSession OpenIyzicoSession(CheckoutPageState pageState)
    {
        var now = DateTime.UtcNow;
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(), SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, BillingProvider.Iyzico, now, now.AddMinutes(30));
        session.SetRedirectReady("https://sandbox.example/checkout?token=t", "t", now);
        _provider.Setup(x => x.GetCheckoutPageStateAsync(session, It.IsAny<CancellationToken>())).ReturnsAsync(pageState);
        _resolver.Setup(x => x.Resolve(BillingProvider.Iyzico)).Returns(_provider.Object);
        return session;
    }

    [Theory]
    [InlineData(CheckoutPageState.Payable)]
    [InlineData(CheckoutPageState.Unusable)]
    public async Task Reconcile_Should_NotActivate_When_NotPaid(CheckoutPageState pageState)
    {
        var session = OpenIyzicoSession(pageState);

        var result = await Create().ReconcileAsync(session, session.TenantId);

        result.Should().Be(pageState);
        _activation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reconcile_Should_Activate_WithProviderVerified_When_Paid()
    {
        var session = OpenIyzicoSession(CheckoutPageState.Paid);
        _activation
            .Setup(x => x.TryActivateAsync(session.Id, null, BillingProvider.Iyzico, null, BillingActivationSource.ProviderVerified, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SubscriptionCheckoutSessionDto>.Failure("x", "y"));

        var result = await Create().ReconcileAsync(session, tenantIdConstraint: null);

        result.Should().Be(CheckoutPageState.Paid, "etkinleştirme başarısız olsa da sayfa durumu ödenmiş olarak döner");
        _activation.Verify(
            x => x.TryActivateAsync(session.Id, null, BillingProvider.Iyzico, null, BillingActivationSource.ProviderVerified, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
