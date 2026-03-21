using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetByIdSpec : Specification<Pet>
{
    public PetByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(p => p.TenantId == tenantId && p.Id == id);
    }
}
