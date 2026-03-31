using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.PetColors.Specs;

public sealed class PetColorByIdSpec : Specification<PetColor>
{
    public PetColorByIdSpec(Guid id)
    {
        Query.Where(c => c.Id == id);
    }
}
