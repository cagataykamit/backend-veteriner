using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetCapabilities;

public sealed class GetDashboardCapabilitiesQueryHandler
    : IRequestHandler<GetDashboardCapabilitiesQuery, Result<DashboardCapabilitiesDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly ICurrentUserPermissionChecker _permissionChecker;
    private readonly ICurrentUserRoleAccessor _roleAccessor;
    private readonly IReadRepository<TenantSubscription> _subscriptions;

    public GetDashboardCapabilitiesQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        ICurrentUserPermissionChecker permissionChecker,
        ICurrentUserRoleAccessor roleAccessor,
        IReadRepository<TenantSubscription> subscriptions)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _permissionChecker = permissionChecker;
        _roleAccessor = roleAccessor;
        _subscriptions = subscriptions;
    }

    public async Task<Result<DashboardCapabilitiesDto>> Handle(GetDashboardCapabilitiesQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<DashboardCapabilitiesDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var selectedClinicId = _clinicContext.ClinicId;
        var roleNames = _roleAccessor.GetRoleNames();

        var isOwner = HasRole(roleNames, "Owner");
        var isAdmin = HasAnyRole(roleNames, "Admin", "ClinicAdmin", "PlatformAdmin");
        var isStaff = HasAnyRole(roleNames, "Staff", "Veteriner", "Sekreter");
        var canViewOperationalAlerts = _permissionChecker.HasPermission(PermissionCatalog.Dashboard.Read);
        // Conservative mapping: finance widgeti için explicit Payments.Read arıyoruz.
        var canViewFinance = _permissionChecker.HasPermission(PermissionCatalog.Payments.Read);

        var isTenantReadOnly = false;
        var sub = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        if (sub is not null)
        {
            var effectiveStatus = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, DateTime.UtcNow);
            isTenantReadOnly = effectiveStatus == TenantSubscriptionStatus.ReadOnly;
        }

        var dto = new DashboardCapabilitiesDto(
            CanViewFinance: canViewFinance,
            CanViewOperationalAlerts: canViewOperationalAlerts,
            IsOwner: isOwner,
            IsAdmin: isAdmin,
            IsStaff: isStaff,
            SelectedClinicId: selectedClinicId,
            HasClinicContext: selectedClinicId.HasValue,
            IsTenantReadOnly: isTenantReadOnly);

        return Result<DashboardCapabilitiesDto>.Success(dto);
    }

    private static bool HasRole(IReadOnlyList<string> roles, string expected)
        => roles.Any(r => string.Equals(r, expected, StringComparison.OrdinalIgnoreCase));

    private static bool HasAnyRole(IReadOnlyList<string> roles, params string[] expectedRoles)
        => expectedRoles.Any(expected => HasRole(roles, expected));
}
