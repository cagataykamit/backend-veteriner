using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Pets.Queries.GetList;

public sealed class GetPetsListQueryHandler
    : IRequestHandler<GetPetsListQuery, Result<PagedResult<PetListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IPetReadModelReader _readModelReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ILogger<GetPetsListQueryHandler> _logger;

    public GetPetsListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IPetReadModelReader readModelReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        ILogger<GetPetsListQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _pets = pets;
        _clients = clients;
        _readModelReader = readModelReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _logger = logger ?? NullLogger<GetPetsListQueryHandler>.Instance;
    }

    public async ValueTask<Result<PagedResult<PetListItemDto>>> Handle(GetPetsListQuery request, CancellationToken ct)
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
        var searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);

        // Okuma modelinde durum kolonu yok: durum süzgeci verildiğinde komut veritabanından okunur.
        if (_queryReadModelsOptions.PetsEnabled && request.Status is null)
        {
            return await HandleFromQueryReadModelAsync(
                tenantId, page, pageSize, request.ClientId, request.SpeciesId, searchPattern, ct);
        }

        return await HandleFromCommandDbAsync(
            tenantId, page, pageSize, request.ClientId, request.SpeciesId, request.Status,
            DailySearchTerm.Create(request.PageRequest.Search), ct);
    }

    private async Task<Result<PagedResult<PetListItemDto>>> HandleFromQueryReadModelAsync(
        Guid tenantId,
        int page,
        int pageSize,
        Guid? clientId,
        Guid? speciesId,
        string? searchPattern,
        CancellationToken ct)
    {
        var readResult = await _readModelReader.GetListAsync(
            new PetListReadRequest(tenantId, page, pageSize, clientId, speciesId, searchPattern),
            ct);

        _logger.LogInformation(
            "Pets list generated from query read-model. TenantId={TenantId} Page={Page} PageSize={PageSize} TotalItems={TotalItems}",
            tenantId,
            page,
            pageSize,
            readResult.TotalCount);

        // Durum okuma modelinde yok; sayfadaki satırların durumu komut veritabanından doldurulur.
        var items = readResult.Items;
        if (items.Count > 0)
        {
            var statusById = (await _pets.ListAsync(
                    new PetsByTenantIdsSpec(tenantId, items.Select(i => i.Id).ToArray()), ct))
                .ToDictionary(p => p.Id, p => p.Status);
            items = items
                .Select(i => i with { Status = statusById.GetValueOrDefault(i.Id, PetStatus.Active) })
                .ToList();
        }

        return Result<PagedResult<PetListItemDto>>.Success(
            PagedResult<PetListItemDto>.Create(items, readResult.TotalCount, page, pageSize));
    }

    private async Task<Result<PagedResult<PetListItemDto>>> HandleFromCommandDbAsync(
        Guid tenantId,
        int page,
        int pageSize,
        Guid? clientId,
        Guid? speciesId,
        PetStatus? status,
        DailySearchTerm? searchTerm,
        CancellationToken ct)
    {
        Guid[] petIdsFromClientText = [];
        if (searchTerm is not null)
        {
            var matchedClients = await _clients.ListAsync(new ClientsByTenantTextSearchSpec(tenantId, searchTerm), ct);
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
                clientId,
                speciesId,
                status,
                searchTerm,
                petIdsFromClientText),
            ct);
        var rows = await _pets.ListAsync(
            new PetsByTenantPagedSpec(
                tenantId,
                page,
                pageSize,
                clientId,
                speciesId,
                status,
                searchTerm,
                petIdsFromClientText),
            ct);

        var items = rows
            .Select(r => new PetListItemDto(
                r.Id,
                r.TenantId,
                r.ClientId,
                r.Name,
                r.SpeciesId,
                r.SpeciesName,
                r.ColorId,
                r.ColorName,
                r.Breed,
                r.Weight ?? 0,
                r.Status))
            .ToList();

        return Result<PagedResult<PetListItemDto>>.Success(
            PagedResult<PetListItemDto>.Create(items, total, page, pageSize));
    }
}
