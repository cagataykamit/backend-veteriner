using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetsByTenantCountSpec : Specification<Pet>
{
    public PetsByTenantCountSpec(Guid tenantId)
    {
        Query.Where(p => p.TenantId == tenantId);
    }
}
