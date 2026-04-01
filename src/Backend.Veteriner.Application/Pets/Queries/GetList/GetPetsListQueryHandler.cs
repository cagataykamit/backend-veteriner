using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetList;

public sealed class GetPetsListQueryHandler
    : IRequestHandler<GetPetsListQuery, Result<PagedResult<PetListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetPetsListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _pets = pets;
        _clients = clients;
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

        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        string? searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);
        Guid[] petIdsFromClientText = [];
        if (searchPattern is not null)
        {
            var matchedClients = await _clients.ListAsync(new ClientsByTenantTextSearchSpec(tenantId, searchPattern), ct);
            var clientIds = matchedClients.Select(c => c.Id).Distinct().ToArray();
            if (clientIds.Length > 0)
            {
                var owned = await _pets.ListAsync(new PetsByTenantForClientIdsSpec(tenantId, clientIds), ct);
                petIdsFromClientText = owned.Select(p => p.Id).Distinct().ToArray();
            }
        }

        var total = await _pets.CountAsync(
            new PetsByTenantCountSpec(
                tenantId,
                request.ClientId,
                request.SpeciesId,
                searchPattern,
                petIdsFromClientText),
            ct);
        var rows = await _pets.ListAsync(
            new PetsByTenantPagedSpec(
                tenantId,
                page,
                pageSize,
                request.ClientId,
                request.SpeciesId,
                searchPattern,
                petIdsFromClientText),
            ct);

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
