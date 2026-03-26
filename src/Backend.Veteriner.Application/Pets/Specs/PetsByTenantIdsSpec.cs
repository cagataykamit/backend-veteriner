using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetsByTenantIdsSpec : Specification<Pet>
{
    public PetsByTenantIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds)
    {
        Query.Where(p => p.TenantId == tenantId && petIds.Contains(p.Id));
    }
}