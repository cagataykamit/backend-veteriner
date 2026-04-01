using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetsByTenantForClientIdsSpec : Specification<Pet>
{
    public PetsByTenantForClientIdsSpec(Guid tenantId, Guid[] clientIds)
    {
        Query.Where(p => p.TenantId == tenantId && clientIds.Contains(p.ClientId));
    }
}
