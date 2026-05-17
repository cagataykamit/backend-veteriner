using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;
using Backend.Veteriner.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Veteriner.IntegrationTests.Catalog;

public sealed class CatalogListCacheTests
{
    private static CatalogListCache CreateCache()
        => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void SpeciesList_Should_RoundTrip_And_Invalidate()
    {
        var cache = CreateCache();
        var paged = PagedResult<SpeciesListItemDto>.Create(
            [new SpeciesListItemDto(Guid.NewGuid(), "DOG", "Köpek", true, 1)],
            1,
            1,
            20);

        cache.SetSpeciesList(null, 1, 20, paged);
        cache.TryGetSpeciesList(null, 1, 20, out var hit).Should().BeTrue();
        hit.Should().BeSameAs(paged);

        cache.InvalidateSpecies();
        cache.TryGetSpeciesList(null, 1, 20, out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidateSpecies_Should_Also_Clear_Breeds()
    {
        var cache = CreateCache();
        var breeds = PagedResult<BreedListItemDto>.Create([], 0, 1, 20);
        cache.SetBreedsList(null, null, null, 1, 20, breeds);

        cache.InvalidateSpecies();
        cache.TryGetBreedsList(null, null, null, 1, 20, out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidateBreeds_Should_Not_Clear_Species()
    {
        var cache = CreateCache();
        var species = PagedResult<SpeciesListItemDto>.Create([], 0, 1, 20);
        cache.SetSpeciesList(null, 1, 20, species);
        cache.SetBreedsList(null, null, null, 1, 20, PagedResult<BreedListItemDto>.Create([], 0, 1, 20));

        cache.InvalidateBreeds();

        cache.TryGetSpeciesList(null, 1, 20, out var speciesHit).Should().BeTrue();
        speciesHit.Should().NotBeNull();
        cache.TryGetBreedsList(null, null, null, 1, 20, out _).Should().BeFalse();
    }

    [Fact]
    public void BreedsList_Should_UseDistinctKeys_For_DifferentFilters()
    {
        var cache = CreateCache();
        var sid = Guid.NewGuid();
        var all = PagedResult<BreedListItemDto>.Create([], 0, 1, 20);
        var filtered = PagedResult<BreedListItemDto>.Create([], 0, 1, 20);

        cache.SetBreedsList(null, null, null, 1, 20, all);
        cache.SetBreedsList(true, sid, "golden", 1, 20, filtered);

        cache.TryGetBreedsList(null, null, null, 1, 20, out var allHit).Should().BeTrue();
        allHit.Should().BeSameAs(all);
        cache.TryGetBreedsList(true, sid, "golden", 1, 20, out var filteredHit).Should().BeTrue();
        filteredHit.Should().BeSameAs(filtered);
    }
}
