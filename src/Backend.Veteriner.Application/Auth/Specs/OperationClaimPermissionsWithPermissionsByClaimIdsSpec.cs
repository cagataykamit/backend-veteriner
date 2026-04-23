using Ardalis.Specification;
using Backend.Veteriner.Domain.Auth;

namespace Backend.Veteriner.Application.Auth.Specs;

/// <summary>
/// Birden çok <see cref="OperationClaim"/> için <see cref="OperationClaimPermission"/> + <see cref="Domain.Authorization.Permission"/> yükler.
/// </summary>
public sealed class OperationClaimPermissionsWithPermissionsByClaimIdsSpec : Specification<OperationClaimPermission>
{
    public OperationClaimPermissionsWithPermissionsByClaimIdsSpec(IReadOnlyList<Guid> operationClaimIds)
    {
        Query.AsNoTracking();
        Query.Include(x => x.Permission!);
        if (operationClaimIds.Count > 0)
            Query.Where(x => operationClaimIds.Contains(x.OperationClaimId));
        else
            Query.Where(_ => false);
    }
}
