using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed record PetNameClientRow(Guid Id, Guid ClientId, string Name);

public sealed class PetsByTenantIdsNameClientSpec : Specification<Pet, PetNameClientRow>
{
    public PetsByTenantIdsNameClientSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && petIds.Contains(p.Id))
            .Select(p => new PetNameClientRow(p.Id, p.ClientId, p.Name));
    }
}
