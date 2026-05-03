using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Queries.GetAccessState;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class GetTenantAccessStateQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IUserTenantRepository> _userTenants = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();

    private GetTenantAccessStateQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _client.Object,
            _userTenants.Object,
            _tenants.Object,
            _subscriptions.Object);

    private static void SetTenantId(Tenant t, Guid id)
        => typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(t, id);

    [Fact]
    public async Task Member_ActiveSubscription_Should_Return_NotReadOnly()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _client.SetupGet(c => c.UserId).Returns(uid);
        _userTenants.Setup(r => r.ExistsAsync(uid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var tenant = new Tenant("T");
        SetTenantId(tenant, tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var sub = TenantSubscription.StartTrial(
            tid,
            SubscriptionPlanCode.Basic,
            DateTime.UtcNow,
            trialDays: 30);
        _subscriptions.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<TenantSubscriptionByTenantIdSpec>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);

        var result = await CreateHandler().Handle(new GetTenantAccessStateQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TenantId.Should().Be(tid);
        result.Value.IsReadOnly.Should().BeFalse();
        result.Value.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task Member_ReadOnlySubscription_Should_Return_ReadOnly()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _client.SetupGet(c => c.UserId).Returns(uid);
        _userTenants.Setup(r => r.ExistsAsync(uid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var tenant = new Tenant("T");
        SetTenantId(tenant, tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow, 30);
        typeof(TenantSubscription).GetProperty(nameof(TenantSubscription.Status))!
            .SetValue(sub, TenantSubscriptionStatus.ReadOnly);

        _subscriptions.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<TenantSubscriptionByTenantIdSpec>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);

        var result = await CreateHandler().Handle(new GetTenantAccessStateQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsReadOnly.Should().BeTrue();
        result.Value.ReasonCode.Should().Be("Subscriptions.TenantReadOnly");
    }

    [Fact]
    public async Task JwtTenantMismatch_Should_Return_AccessDenied()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(new GetTenantAccessStateQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task NotMember_Should_Return_TenantNotMember()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _client.SetupGet(c => c.UserId).Returns(uid);
        _userTenants.Setup(r => r.ExistsAsync(uid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateHandler().Handle(new GetTenantAccessStateQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.TenantNotMember");
    }

    [Fact]
    public async Task MissingSubscription_Should_Return_NotFound()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _client.SetupGet(c => c.UserId).Returns(uid);
        _userTenants.Setup(r => r.ExistsAsync(uid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var tenant = new Tenant("T");
        SetTenantId(tenant, tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _subscriptions.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<TenantSubscriptionByTenantIdSpec>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantSubscription?)null);

        var result = await CreateHandler().Handle(new GetTenantAccessStateQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.NotFound");
    }
}
