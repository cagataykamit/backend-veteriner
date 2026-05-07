using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.BreedsReference.Specs;

public sealed class BreedsCountSpec : Specification<Breed>
{
    /// <summary>Liste filtresi: null ise `IsActive` ile sınırlama yok.</summary>
    public bool? IsActiveFilter { get; }

    /// <summary>Liste filtresi: null ise tür ile sınırlama yok.</summary>
    public Guid? SpeciesIdFilter { get; }

    /// <summary>İşlenmiş arama (küçük harf); null ise metin araması yok.</summary>
    public string? SearchTermLower { get; }

    public BreedsCountSpec(bool? isActive, Guid? speciesId, string? searchTermLower)
    {
        IsActiveFilter = isActive;
        SpeciesIdFilter = speciesId;
        SearchTermLower = searchTermLower;
        Query.AsNoTracking();
        if (isActive.HasValue)
            Query.Where(b => b.IsActive == isActive.Value);
        if (speciesId.HasValue)
            Query.Where(b => b.SpeciesId == speciesId.Value);
        if (!string.IsNullOrEmpty(searchTermLower))
        {
            var pat = ListQueryTextSearch.BuildContainsLikePattern(searchTermLower);
            Query.Where(b =>
                EF.Functions.Like(b.Name, pat)
                || EF.Functions.Like(b.Species!.Name, pat));
        }
    }
}
