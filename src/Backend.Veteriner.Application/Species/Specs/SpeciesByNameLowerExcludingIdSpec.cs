using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesByNameLowerExcludingIdSpec : Specification<Species>
{
    public SpeciesByNameLowerExcludingIdSpec(string nameLowerInvariant, Guid excludeId)
    {
        Query.Where(s => s.Name.ToLower() == nameLowerInvariant && s.Id != excludeId);
    }
}
