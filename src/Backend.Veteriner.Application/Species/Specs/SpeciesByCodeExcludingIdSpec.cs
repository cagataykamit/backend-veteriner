using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesByCodeExcludingIdSpec : Specification<Species>
{
    public SpeciesByCodeExcludingIdSpec(string codeNormalizedUpperInvariant, Guid excludeId)
    {
        Query.Where(s => s.Code == codeNormalizedUpperInvariant && s.Id != excludeId);
    }
}
