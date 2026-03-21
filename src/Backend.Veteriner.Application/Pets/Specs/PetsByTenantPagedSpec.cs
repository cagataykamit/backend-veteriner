using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetsByTenantPagedSpec : Specification<Pet>
{
    public PetsByTenantPagedSpec(Guid tenantId, int page, int pageSize)
    {
        Query.Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Species)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
