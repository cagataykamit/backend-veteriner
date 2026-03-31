using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesByIdsSpec : Specification<Species>
{
    public SpeciesByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Query.Where(s => ids.Contains(s.Id));
    }
}
