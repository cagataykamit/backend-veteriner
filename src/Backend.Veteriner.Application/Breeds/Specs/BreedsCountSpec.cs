using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.BreedsReference.Specs;

public sealed class BreedsCountSpec : Specification<Breed>
{
    public BreedsCountSpec(bool? isActive, Guid? speciesId)
    {
        if (isActive.HasValue)
            Query.Where(b => b.IsActive == isActive.Value);
        if (speciesId.HasValue)
            Query.Where(b => b.SpeciesId == speciesId.Value);
    }
}
