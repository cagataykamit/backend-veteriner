using Ardalis.Specification;
using Backend.Veteriner.Domain.Auth;

namespace Backend.Veteriner.Application.Auth.Specs;

public sealed class OperationClaimByIdSpec : Specification<OperationClaim>
{
    public OperationClaimByIdSpec(Guid id)
    {
        Query.Where(x => x.Id == id);
    }
}
