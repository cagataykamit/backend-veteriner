using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionSummary;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class GetTenantSubscriptionSummaryQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();
    private readonly Mock<IReadRepository<ScheduledSubscriptionPlanChange>> _planChanges = new();

    private GetTenantSubscriptionSummaryQueryHandler CreateHandler()
    {
        var eval = new TenantSubscriptionEffectiveWriteEvaluator(_tenants.Object, _subscriptions.Object);
        return new(_tenantContext.Object, _permissions.Object, _tenants.Object, _subscriptions.Object, _planChanges.Object, eval);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetTenantSubscriptionSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_NoSubscriptionOrTenantReadPermission()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(false);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetTenantSubscriptionSummaryQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_CrossTenant_Without_TenantsRead()
    {
        var jwtTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(jwtTenant);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(true);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(false);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetTenantSubscriptionSummaryQuery(otherTenant), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(true);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(false);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetTenantSubscriptionSummaryQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SubscriptionNotFound()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("Acme");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(true);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantSubscription?)null);
        _planChanges.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetTenantSubscriptionSummaryQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_When_SameTenant_And_SubscriptionsRead()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("Acme");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(true);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Create)).Returns(true);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChanges.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetTenantSubscriptionSummaryQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TenantId.Should().Be(tid);
        result.Value.PlanCode.Should().Be("Basic");
        result.Value.CanManageSubscription.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Allow_CrossTenant_When_TenantsRead()
    {
        var jwtTenant = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tenant = new Tenant("Other");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, otherTenantId);
        var sub = TenantSubscription.StartTrial(otherTenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _tenantContext.SetupGet(x => x.TenantId).Returns(jwtTenant);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(true);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Create)).Returns(false);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChanges.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetTenantSubscriptionSummaryQuery(otherTenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TenantId.Should().Be(otherTenantId);
    }
}
