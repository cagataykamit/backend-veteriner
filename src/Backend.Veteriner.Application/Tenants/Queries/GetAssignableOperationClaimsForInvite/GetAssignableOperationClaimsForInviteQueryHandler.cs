using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetAssignableOperationClaimsForInvite;

public sealed class GetAssignableOperationClaimsForInviteQueryHandler
    : IRequestHandler<GetAssignableOperationClaimsForInviteQuery, Result<IReadOnlyList<AssignableOperationClaimForInviteDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<OperationClaim> _claims;

    public GetAssignableOperationClaimsForInviteQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<OperationClaim> claims)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _claims = claims;
    }

    public async Task<Result<IReadOnlyList<AssignableOperationClaimForInviteDto>>> Handle(
        GetAssignableOperationClaimsForInviteQuery request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<IReadOnlyList<AssignableOperationClaimForInviteDto>>.Failure(
                "Auth.PermissionDenied",
                "Atanabilir rolleri listelemek için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId || jwtTenantId != request.TenantId)
        {
            return Result<IReadOnlyList<AssignableOperationClaimForInviteDto>>.Failure(
                "Tenants.AccessDenied",
                "Bu liste yalnızca oturumdaki kiracı bağlamında alınabilir.");
        }

        var allowed = InviteAssignableOperationClaimsCatalog.NamesInDisplayOrder;
        var lower = allowed.Select(n => n.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        var fromDb = await _claims.ListAsync(
            new AssignableInviteOperationClaimsByNameSetSpec(lower),
            ct);

        var byName = fromDb.ToDictionary(
            c => c.Name,
            StringComparer.OrdinalIgnoreCase);

        var ordered = new List<AssignableOperationClaimForInviteDto>();
        foreach (var name in allowed)
        {
            if (byName.TryGetValue(name, out var claim))
                ordered.Add(new AssignableOperationClaimForInviteDto(claim.Id, claim.Name));
        }

        return Result<IReadOnlyList<AssignableOperationClaimForInviteDto>>.Success(ordered);
    }
}
