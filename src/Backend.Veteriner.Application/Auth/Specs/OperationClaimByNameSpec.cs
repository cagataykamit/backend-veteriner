using Ardalis.Specification;
using Backend.Veteriner.Domain.Auth;

namespace Backend.Veteriner.Application.Auth.Specs;

public sealed class OperationClaimByNameSpec : Specification<OperationClaim>
{
    public OperationClaimByNameSpec(string normalizedNameLowerInvariant)
    {
        Query.Where(x => x.Name.ToLower() == normalizedNameLowerInvariant);
    }
}
