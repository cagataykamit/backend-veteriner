using System.Collections.Concurrent;
using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Veteriner.Infrastructure.Caching;

/// <summary>
/// Species/breed katalog liste cache (IMemoryCache + key registry ile invalidation).
/// </summary>
public sealed class CatalogListCache : ICatalogListCache, ICatalogCacheInvalidator
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _speciesKeys = new();
    private readonly ConcurrentDictionary<string, byte> _breedsKeys = new();

    public CatalogListCache(IMemoryCache cache) => _cache = cache;

    public bool TryGetSpeciesList(bool? isActive, int page, int pageSize, out PagedResult<SpeciesListItemDto>? result)
    {
        var key = CatalogCacheKeys.SpeciesList(isActive, page, pageSize);
        if (_cache.TryGetValue(key, out PagedResult<SpeciesListItemDto>? cached) && cached is not null)
        {
            result = cached;
            return true;
        }

        result = null;
        return false;
    }

    public void SetSpeciesList(bool? isActive, int page, int pageSize, PagedResult<SpeciesListItemDto> result)
    {
        var key = CatalogCacheKeys.SpeciesList(isActive, page, pageSize);
        _speciesKeys.TryAdd(key, 0);
        _cache.Set(key, result, CatalogCacheKeys.ListTtl);
    }

    public bool TryGetBreedsList(
        bool? isActive,
        Guid? speciesId,
        string? searchTermLower,
        int page,
        int pageSize,
        out PagedResult<BreedListItemDto>? result)
    {
        var key = CatalogCacheKeys.BreedsList(isActive, speciesId, searchTermLower, page, pageSize);
        if (_cache.TryGetValue(key, out PagedResult<BreedListItemDto>? cached) && cached is not null)
        {
            result = cached;
            return true;
        }

        result = null;
        return false;
    }

    public void SetBreedsList(
        bool? isActive,
        Guid? speciesId,
        string? searchTermLower,
        int page,
        int pageSize,
        PagedResult<BreedListItemDto> result)
    {
        var key = CatalogCacheKeys.BreedsList(isActive, speciesId, searchTermLower, page, pageSize);
        _breedsKeys.TryAdd(key, 0);
        _cache.Set(key, result, CatalogCacheKeys.ListTtl);
    }

    public void InvalidateSpecies()
    {
        RemoveRegisteredKeys(_speciesKeys);
        InvalidateBreeds();
    }

    public void InvalidateBreeds()
        => RemoveRegisteredKeys(_breedsKeys);

    private void RemoveRegisteredKeys(ConcurrentDictionary<string, byte> registry)
    {
        foreach (var key in registry.Keys)
        {
            _cache.Remove(key);
            registry.TryRemove(key, out _);
        }
    }
}
