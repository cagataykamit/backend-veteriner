using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.BreedsReference.Specs;

public sealed class BreedBySpeciesAndNameLowerSpec : Specification<Breed>
{
    public BreedBySpeciesAndNameLowerSpec(Guid speciesId, string nameLowerInvariant)
    {
        Query.Where(b => b.SpeciesId == speciesId && b.Name.ToLower() == nameLowerInvariant);
    }
}
