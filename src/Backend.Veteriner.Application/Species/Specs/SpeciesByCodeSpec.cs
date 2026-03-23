using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesByCodeSpec : Specification<Species>
{
    public SpeciesByCodeSpec(string codeNormalizedUpperInvariant)
    {
        Query.Where(s => s.Code == codeNormalizedUpperInvariant);
    }
}
