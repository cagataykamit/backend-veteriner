using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesCountSpec : Specification<Species>
{
    /// <summary>Liste filtresi: null ise tüm kayıtlar sayılır.</summary>
    public bool? IsActiveFilter { get; }

    public SpeciesCountSpec(bool? isActive)
    {
        IsActiveFilter = isActive;
        if (isActive.HasValue)
            Query.Where(s => s.IsActive == isActive.Value);
    }
}
