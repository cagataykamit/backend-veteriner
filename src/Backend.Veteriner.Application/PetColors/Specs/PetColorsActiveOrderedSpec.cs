using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.PetColors.Specs;

public sealed class PetColorsActiveOrderedSpec : Specification<PetColor>
{
    public PetColorsActiveOrderedSpec()
    {
        Query
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name);
    }
}
