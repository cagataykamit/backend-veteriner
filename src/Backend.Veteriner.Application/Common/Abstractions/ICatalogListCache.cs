using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Global species/breed katalog liste sonuçları için process-local memory cache (cache-aside).
/// </summary>
public interface ICatalogListCache
{
    bool TryGetSpeciesList(bool? isActive, int page, int pageSize, out PagedResult<SpeciesListItemDto>? result);

    void SetSpeciesList(bool? isActive, int page, int pageSize, PagedResult<SpeciesListItemDto> result);

    bool TryGetBreedsList(
        bool? isActive,
        Guid? speciesId,
        string? searchTermLower,
        int page,
        int pageSize,
        out PagedResult<BreedListItemDto>? result);

    void SetBreedsList(
        bool? isActive,
        Guid? speciesId,
        string? searchTermLower,
        int page,
        int pageSize,
        PagedResult<BreedListItemDto> result);
}
