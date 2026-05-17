using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SpeciesReference.Queries.GetList;

public sealed class GetSpeciesListQueryHandler
    : IRequestHandler<GetSpeciesListQuery, Result<PagedResult<SpeciesListItemDto>>>
{
    private readonly IReadRepository<Species> _species;
    private readonly ICatalogListCache _catalogCache;

    public GetSpeciesListQueryHandler(IReadRepository<Species> species, ICatalogListCache catalogCache)
    {
        _species = species;
        _catalogCache = catalogCache;
    }

    public async Task<Result<PagedResult<SpeciesListItemDto>>> Handle(GetSpeciesListQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        if (_catalogCache.TryGetSpeciesList(request.IsActive, page, pageSize, out var cached) && cached is not null)
            return Result<PagedResult<SpeciesListItemDto>>.Success(cached);

        var total = await _species.CountAsync(new SpeciesCountSpec(request.IsActive), ct);
        var rows = await _species.ListAsync(new SpeciesPagedSpec(page, pageSize, request.IsActive), ct);

        var items = rows
            .Select(s => new SpeciesListItemDto(s.Id, s.Code, s.Name, s.IsActive, s.DisplayOrder))
            .ToList();

        var paged = PagedResult<SpeciesListItemDto>.Create(items, total, page, pageSize);
        _catalogCache.SetSpeciesList(request.IsActive, page, pageSize, paged);
        return Result<PagedResult<SpeciesListItemDto>>.Success(paged);
    }
}
