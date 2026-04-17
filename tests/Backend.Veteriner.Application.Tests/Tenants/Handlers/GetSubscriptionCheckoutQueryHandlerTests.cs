using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionCheckout;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class GetSubscriptionCheckoutQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IRepository<BillingCheckoutSession>> _sessionsWrite = new();
    private readonly Mock<IReadRepository<BillingCheckoutSession>> _sessionsRead = new();

    private GetSubscriptionCheckoutQueryHandler CreateHandler()
        => new(_tenantContext.Object, _sessionsWrite.Object, _sessionsRead.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantMismatch()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(a);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSubscriptionCheckoutQuery(b, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SessionNotFound()
    {
        var tid = Guid.NewGuid();
        var sid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingCheckoutSession?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSubscriptionCheckoutQuery(tid, sid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.CheckoutSessionNotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_When_SessionExists()
    {
        var tid = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var session = BillingCheckoutSession.CreatePending(
            tid,
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Manual,
            now,
            now.AddMinutes(30));

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSubscriptionCheckoutQuery(tid, session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CheckoutSessionId.Should().Be(session.Id);
        result.Value.TargetPlanCode.Should().Be("Pro");
    }
}
