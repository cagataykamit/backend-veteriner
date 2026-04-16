using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.SpeciesReference.Specs;

public sealed class SpeciesPagedSpec : Specification<Species>
{
    /// <summary>Liste filtresi: null ise tüm kayıtlar döner.</summary>
    public bool? IsActiveFilter { get; }

    public SpeciesPagedSpec(int page, int pageSize, bool? isActive)
    {
        IsActiveFilter = isActive;
        if (isActive.HasValue)
            Query.Where(s => s.IsActive == isActive.Value);

        Query.OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
