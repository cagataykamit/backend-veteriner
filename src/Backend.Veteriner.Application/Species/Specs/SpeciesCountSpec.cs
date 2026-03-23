using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesCountSpec : Specification<Species>
{
    public SpeciesCountSpec(bool? isActive)
    {
        if (isActive.HasValue)
            Query.Where(s => s.IsActive == isActive.Value);
    }
}
