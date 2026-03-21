using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetList;

public sealed class GetPetsListQueryHandler
    : IRequestHandler<GetPetsListQuery, Result<PagedResult<PetListItemDto>>>
{
    private readonly IReadRepository<Pet> _pets;

    public GetPetsListQueryHandler(IReadRepository<Pet> pets) => _pets = pets;

    public async Task<Result<PagedResult<PetListItemDto>>> Handle(GetPetsListQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _pets.CountAsync(new PetsByTenantCountSpec(request.TenantId), ct);
        var rows = await _pets.ListAsync(new PetsByTenantPagedSpec(request.TenantId, page, pageSize), ct);

        var items = rows
            .Select(p => new PetListItemDto(p.Id, p.TenantId, p.ClientId, p.Name, p.Species, p.Breed))
            .ToList();

        return Result<PagedResult<PetListItemDto>>.Success(
            PagedResult<PetListItemDto>.Create(items, total, page, pageSize));
    }
}
