using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed record PetNameClientSpeciesRow(Guid Id, Guid ClientId, string Name, Guid SpeciesId, string SpeciesName);

public sealed class PetsByTenantIdsNameClientSpeciesSpec : Specification<Pet, PetNameClientSpeciesRow>
{
    public PetsByTenantIdsNameClientSpeciesSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && petIds.Contains(p.Id))
            .Select(p => new PetNameClientSpeciesRow(
                p.Id,
                p.ClientId,
                p.Name,
                p.SpeciesId,
                p.Species != null ? p.Species.Name : string.Empty));
    }
}
