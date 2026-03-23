using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesByNameLowerSpec : Specification<Species>
{
    public SpeciesByNameLowerSpec(string nameLowerInvariant)
    {
        Query.Where(s => s.Name.ToLower() == nameLowerInvariant);
    }
}
