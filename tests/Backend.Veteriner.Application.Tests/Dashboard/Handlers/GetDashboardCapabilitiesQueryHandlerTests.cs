using Ardalis.Specification;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Queries.GetCapabilities;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class GetDashboardCapabilitiesQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<ICurrentUserRoleAccessor> _roles = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subs = new();

    private GetDashboardCapabilitiesQueryHandler CreateHandler()
        => new(
            _tenant.Object,
            _clinic.Object,
            _permissions.Object,
            _roles.Object,
            _subs.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardCapabilitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_SetFinanceCapability_FromPaymentsReadPermission()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _roles.Setup(r => r.GetRoleNames()).Returns(Array.Empty<string>());
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Dashboard.Read)).Returns(true);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Payments.Read)).Returns(true);
        _subs.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantSubscription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantSubscription?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardCapabilitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CanViewFinance.Should().BeTrue();
        result.Value.CanViewOperationalAlerts.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_SetFinanceCapabilityFalse_WhenPaymentsReadMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _roles.Setup(r => r.GetRoleNames()).Returns(Array.Empty<string>());
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Dashboard.Read)).Returns(true);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Payments.Read)).Returns(false);
        _subs.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantSubscription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantSubscription?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardCapabilitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CanViewFinance.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_MapRoleAndClinicFlags()
    {
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        _roles.Setup(r => r.GetRoleNames()).Returns(["Owner", "ClinicAdmin", "Veteriner"]);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Dashboard.Read)).Returns(true);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Payments.Read)).Returns(false);
        _subs.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantSubscription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantSubscription?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardCapabilitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.IsOwner.Should().BeTrue();
        dto.IsAdmin.Should().BeTrue();
        dto.IsStaff.Should().BeTrue();
        dto.SelectedClinicId.Should().Be(cid);
        dto.HasClinicContext.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_SetTenantReadOnly_WhenEffectiveStatusReadOnly()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        _roles.Setup(r => r.GetRoleNames()).Returns(Array.Empty<string>());
        _permissions.Setup(p => p.HasPermission(It.IsAny<string>())).Returns(false);
        _subs.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantSubscription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSubscription.StartTrial(tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-40), 14));

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardCapabilitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsTenantReadOnly.Should().BeTrue();
    }
}
