using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Commands.Checkout;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class CancelSubscriptionCheckoutCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IRepository<BillingCheckoutSession>> _sessionsWrite = new();
    private readonly Mock<IReadRepository<BillingCheckoutSession>> _sessionsRead = new();

    private CancelSubscriptionCheckoutCommandHandler CreateHandler()
        => new(_tenantContext.Object, _sessionsWrite.Object, _sessionsRead.Object, TimeProvider.System);

    private static BillingCheckoutSession OpenSession(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        var session = BillingCheckoutSession.CreatePending(
            tenantId,
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Iyzico,
            now,
            now.AddMinutes(30));
        session.SetRedirectReady("https://sandbox.example/checkout?token=t", "t", now);
        return session;
    }

    private void ReturnSession(BillingCheckoutSession? session)
        => _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantMismatch()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(new CancelSubscriptionCheckoutCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SessionNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        ReturnSession(null);

        var result = await CreateHandler().Handle(new CancelSubscriptionCheckoutCommand(tid, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.CheckoutSessionNotFound");
    }

    [Fact]
    public async Task Handle_Should_CancelOpenSession_AndSave()
    {
        var tid = Guid.NewGuid();
        var session = OpenSession(tid);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        ReturnSession(session);

        var result = await CreateHandler().Handle(new CancelSubscriptionCheckoutCommand(tid, session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(BillingCheckoutSessionStatus.Cancelled);
        session.IsOpen(DateTime.UtcNow).Should().BeFalse();
        _sessionsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_WithoutWrite_When_SessionAlreadyClosed()
    {
        var tid = Guid.NewGuid();
        var session = OpenSession(tid);
        session.MarkCompleted(DateTime.UtcNow, "ref-1");
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        ReturnSession(session);

        var result = await CreateHandler().Handle(new CancelSubscriptionCheckoutCommand(tid, session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(BillingCheckoutSessionStatus.Completed);
        _sessionsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
