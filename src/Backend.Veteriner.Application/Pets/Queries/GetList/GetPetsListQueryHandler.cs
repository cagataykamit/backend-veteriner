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
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Pet> _pets;

    public GetPetsListQueryHandler(ITenantContext tenantContext, IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _pets = pets;
    }

    public async Task<Result<PagedResult<PetListItemDto>>> Handle(GetPetsListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<PetListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _pets.CountAsync(new PetsByTenantCountSpec(tenantId), ct);
        var rows = await _pets.ListAsync(new PetsByTenantPagedSpec(tenantId, page, pageSize), ct);

        var items = rows
            .Select(p => new PetListItemDto(
                p.Id,
                p.TenantId,
                p.ClientId,
                p.Name,
                p.SpeciesId,
                p.Species?.Name ?? "",
                p.ColorId,
                p.ColorRef?.Name,
                p.Breed,
                p.Weight ?? 0))
            .ToList();

        return Result<PagedResult<PetListItemDto>>.Success(
            PagedResult<PetListItemDto>.Create(items, total, page, pageSize));
    }
}
