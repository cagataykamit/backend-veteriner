using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionSummary;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class GetTenantSubscriptionSummaryQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();
    private readonly Mock<IReadRepository<ScheduledSubscriptionPlanChange>> _planChanges = new();

    private GetTenantSubscriptionSummaryQueryHandler CreateHandler(BillingOptions? billing = null)
    {
        var eval = new TenantSubscriptionEffectiveWriteEvaluator(_tenants.Object, _subscriptions.Object, TimeProvider.System);
        return new(_tenantContext.Object, _permissions.Object, _tenants.Object, _subscriptions.Object, _planChanges.Object, eval, TimeProvider.System,
            Options.Create(billing ?? new BillingOptions()));
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
        var tenant = new Tenant("Acme", DateTime.UtcNow);
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
        var tenant = new Tenant("Acme", DateTime.UtcNow);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(true);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(true);
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
    public async Task Handle_Should_Return_PlanPrices_From_Billing_Configuration()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("Acme", DateTime.UtcNow);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(true);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var billing = new BillingOptions
        {
            PlanPricesMinor = new Dictionary<string, long> { ["Basic"] = 149_000, ["Pro"] = 249_000 },
        };

        var result = await CreateHandler(billing).Handle(new GetTenantSubscriptionSummaryQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var plans = result.Value!.AvailablePlans.ToDictionary(p => p.Code);
        plans["Basic"].MonthlyPriceMinor.Should().Be(149_000);
        plans["Basic"].YearlyPriceMinor.Should().Be(1_490_000, "yıllık = 10 aylık ücret (SubscriptionPricing.YearlyBilledMonths)");
        plans["Pro"].MonthlyPriceMinor.Should().Be(249_000);
        plans["Premium"].MonthlyPriceMinor.Should().BeNull("fiyatı yapılandırılmamış plan fiyatsız döner");
        plans["Premium"].YearlyPriceMinor.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_SetCanManageSubscriptionFalse_When_MissingSubscriptionsManage()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("Acme", DateTime.UtcNow);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(true);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(false);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChanges.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetTenantSubscriptionSummaryQuery(tid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CanManageSubscription.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_Allow_CrossTenant_When_TenantsRead()
    {
        var jwtTenant = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tenant = new Tenant("Other", DateTime.UtcNow);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, otherTenantId);
        var sub = TenantSubscription.StartTrial(otherTenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _tenantContext.SetupGet(x => x.TenantId).Returns(jwtTenant);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(true);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(false);
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
