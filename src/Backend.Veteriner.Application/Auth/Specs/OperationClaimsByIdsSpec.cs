using Ardalis.Specification;
using Backend.Veteriner.Domain.Auth;

namespace Backend.Veteriner.Application.Auth.Specs;

public sealed class OperationClaimsByIdsSpec : Specification<OperationClaim>
{
    public OperationClaimsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
            Query.Where(c => false);
        else
            Query.Where(c => ids.Contains(c.Id));
    }
}
