using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetAssignableRolePermissionMatrix;

/// <summary>
/// Davet ekranı / dokümantasyon için: whitelist rol başına DB’de bağlı permission kodları (read-only).
/// </summary>
public sealed class GetTenantAssignableRolePermissionMatrixQueryHandler
    : IRequestHandler<GetTenantAssignableRolePermissionMatrixQuery,
        Result<IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<OperationClaim> _claims;
    private readonly IReadRepository<OperationClaimPermission> _claimPermissions;

    public GetTenantAssignableRolePermissionMatrixQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<OperationClaim> claims,
        IReadRepository<OperationClaimPermission> claimPermissions)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _claims = claims;
        _claimPermissions = claimPermissions;
    }

    public async Task<Result<IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>>> Handle(
        GetTenantAssignableRolePermissionMatrixQuery request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>>.Failure(
                "Auth.PermissionDenied",
                "Rol-permission matrisi için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>>.Failure(
                "Tenants.AccessDenied",
                "Bu liste yalnızca oturumdaki kiracı bağlamında alınabilir.");
        }

        var allowed = InviteAssignableOperationClaimsCatalog.NamesInDisplayOrder;
        var lower = allowed.Select(n => n.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        var fromDb = await _claims.ListAsync(
            new AssignableInviteOperationClaimsByNameSetSpec(lower),
            ct);

        var byName = fromDb.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var orderedClaims = new List<OperationClaim>();
        foreach (var name in allowed)
        {
            if (byName.TryGetValue(name, out var claim))
                orderedClaims.Add(claim);
        }

        if (orderedClaims.Count == 0)
            return Result<IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>>.Success([]);

        var claimIds = orderedClaims.Select(c => c.Id).Distinct().ToArray();
        var links = await _claimPermissions.ListAsync(
            new OperationClaimPermissionsWithPermissionsByClaimIdsSpec(claimIds),
            ct);

        var perClaim = links
            .GroupBy(x => x.OperationClaimId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<TenantAssignableRolePermissionMatrixRowDto>();
        foreach (var claim in orderedClaims)
        {
            var items = new List<TenantAssignableRolePermissionItemDto>();
            if (perClaim.TryGetValue(claim.Id, out var list))
            {
                foreach (var link in list.OrderBy(x => x.Permission?.Code ?? string.Empty, StringComparer.Ordinal))
                {
                    var p = link.Permission;
                    if (p is null)
                        continue;
                    items.Add(new TenantAssignableRolePermissionItemDto(p.Code, p.Description, p.Group));
                }
            }

            rows.Add(new TenantAssignableRolePermissionMatrixRowDto(claim.Id, claim.Name, items));
        }

        return Result<IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>>.Success(rows);
    }
}
