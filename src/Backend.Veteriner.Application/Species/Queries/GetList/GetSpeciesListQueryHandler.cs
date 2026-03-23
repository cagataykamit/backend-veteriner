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

    public GetSpeciesListQueryHandler(IReadRepository<Species> species) => _species = species;

    public async Task<Result<PagedResult<SpeciesListItemDto>>> Handle(GetSpeciesListQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _species.CountAsync(new SpeciesCountSpec(request.IsActive), ct);
        var rows = await _species.ListAsync(new SpeciesPagedSpec(page, pageSize, request.IsActive), ct);

        var items = rows
            .Select(s => new SpeciesListItemDto(s.Id, s.Code, s.Name, s.IsActive, s.DisplayOrder))
            .ToList();

        return Result<PagedResult<SpeciesListItemDto>>.Success(
            PagedResult<SpeciesListItemDto>.Create(items, total, page, pageSize));
    }
}
