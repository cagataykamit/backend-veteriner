using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Queries.GetList;

public sealed class GetBreedListQueryHandler
    : IRequestHandler<GetBreedListQuery, Result<PagedResult<BreedListItemDto>>>
{
    private readonly IReadRepository<Breed> _breeds;
    private readonly ICatalogListCache _catalogCache;

    public GetBreedListQueryHandler(IReadRepository<Breed> breeds, ICatalogListCache catalogCache)
    {
        _breeds = breeds;
        _catalogCache = catalogCache;
    }

    public async Task<Result<PagedResult<BreedListItemDto>>> Handle(GetBreedListQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var searchTermLower = NormalizeSearchTerm(request.Search);
        if (_catalogCache.TryGetBreedsList(request.IsActive, request.SpeciesId, searchTermLower, page, pageSize, out var cached) && cached is not null)
            return Result<PagedResult<BreedListItemDto>>.Success(cached);

        var total = await _breeds.CountAsync(new BreedsCountSpec(request.IsActive, request.SpeciesId, searchTermLower), ct);
        var rows = await _breeds.ListAsync(new BreedsPagedSpec(page, pageSize, request.IsActive, request.SpeciesId, searchTermLower), ct);

        var items = rows
            .Select(b => new BreedListItemDto(
                b.Id,
                b.SpeciesId,
                b.SpeciesName,
                b.Name,
                b.IsActive))
            .ToList();

        var paged = PagedResult<BreedListItemDto>.Create(items, total, page, pageSize);
        _catalogCache.SetBreedsList(request.IsActive, request.SpeciesId, searchTermLower, page, pageSize, paged);
        return Result<PagedResult<BreedListItemDto>>.Success(paged);
    }

    /// <summary>Boş/whitespace → arama yok; aksi halde trim + küçük harf (Contains ile uyumlu).</summary>
    private static string? NormalizeSearchTerm(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;
        return search.Trim().ToLowerInvariant();
    }
}
