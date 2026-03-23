using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.BreedsReference.Specs;

public sealed class BreedBySpeciesAndNameLowerExcludingIdSpec : Specification<Breed>
{
    public BreedBySpeciesAndNameLowerExcludingIdSpec(Guid speciesId, string nameLowerInvariant, Guid excludeId)
    {
        Query.Where(b =>
            b.SpeciesId == speciesId &&
            b.Name.ToLower() == nameLowerInvariant &&
            b.Id != excludeId);
    }
}
