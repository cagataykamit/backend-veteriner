using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Commands.Checkout;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class FinalizeSubscriptionCheckoutCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ISubscriptionCheckoutActivationService> _activation = new();

    private FinalizeSubscriptionCheckoutCommandHandler CreateHandler()
        => new(_tenantContext.Object, _activation.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantMismatch()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(a);

        var handler = CreateHandler();
        var result = await handler.Handle(new FinalizeSubscriptionCheckoutCommand(b, Guid.NewGuid(), null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
        _activation.Verify(
            x => x.TryActivateAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<BillingProvider?>(), It.IsAny<string?>(), It.IsAny<BillingActivationSource>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Delegate_To_Activation_When_TenantMatches()
    {
        var tid = Guid.NewGuid();
        var sid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var dto = new SubscriptionCheckoutSessionDto(
            sid,
            tid,
            "Basic",
            "Pro",
            BillingCheckoutSessionStatus.Completed,
            BillingProvider.Manual,
            null,
            false,
            null,
            null,
            null,
            null);

        _activation.Setup(x => x.TryActivateAsync(
                sid,
                tid,
                null,
                "ext-ref",
                BillingActivationSource.Manual,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SubscriptionCheckoutSessionDto>.Success(dto));

        var handler = CreateHandler();
        var result = await handler.Handle(new FinalizeSubscriptionCheckoutCommand(tid, sid, "ext-ref"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CheckoutSessionId.Should().Be(sid);
        _activation.Verify(
            x => x.TryActivateAsync(sid, tid, null, "ext-ref", BillingActivationSource.Manual, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
