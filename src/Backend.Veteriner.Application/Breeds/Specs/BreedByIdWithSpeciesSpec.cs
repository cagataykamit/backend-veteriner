using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.BreedsReference.Specs;

public sealed class BreedByIdWithSpeciesSpec : Specification<Breed>
{
    public BreedByIdWithSpeciesSpec(Guid id)
    {
        Query.AsNoTracking();
        Query.Where(b => b.Id == id)
            .Include(b => b.Species!);
    }
}
