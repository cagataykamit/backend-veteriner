using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesByIdSpec : Specification<Species>
{
    public SpeciesByIdSpec(Guid id)
    {
        Query.Where(s => s.Id == id);
    }
}
