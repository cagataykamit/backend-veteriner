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

    public GetBreedListQueryHandler(IReadRepository<Breed> breeds) => _breeds = breeds;

    public async Task<Result<PagedResult<BreedListItemDto>>> Handle(GetBreedListQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _breeds.CountAsync(new BreedsCountSpec(request.IsActive, request.SpeciesId), ct);
        var rows = await _breeds.ListAsync(new BreedsPagedSpec(page, pageSize, request.IsActive, request.SpeciesId), ct);

        var items = rows
            .Select(b => new BreedListItemDto(
                b.Id,
                b.SpeciesId,
                b.Species?.Name ?? "",
                b.Name,
                b.IsActive))
            .ToList();

        return Result<PagedResult<BreedListItemDto>>.Success(
            PagedResult<BreedListItemDto>.Create(items, total, page, pageSize));
    }
}
