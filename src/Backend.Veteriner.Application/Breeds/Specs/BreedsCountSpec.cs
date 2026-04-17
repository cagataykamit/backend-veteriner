using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.BreedsReference.Specs;

public sealed class BreedsCountSpec : Specification<Breed>
{
    /// <summary>Liste filtresi: null ise `IsActive` ile sınırlama yok.</summary>
    public bool? IsActiveFilter { get; }

    /// <summary>Liste filtresi: null ise tür ile sınırlama yok.</summary>
    public Guid? SpeciesIdFilter { get; }

    public BreedsCountSpec(bool? isActive, Guid? speciesId)
    {
        IsActiveFilter = isActive;
        SpeciesIdFilter = speciesId;
        if (isActive.HasValue)
            Query.Where(b => b.IsActive == isActive.Value);
        if (speciesId.HasValue)
            Query.Where(b => b.SpeciesId == speciesId.Value);
    }
}
