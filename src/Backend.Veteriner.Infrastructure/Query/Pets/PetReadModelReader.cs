using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Pets;

public sealed class PetReadModelReader : IPetReadModelReader, IPetReadModelLookupReader
{
    private readonly QueryDbContext _queryDb;

    public PetReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<PetListReadResult> GetListAsync(
        PetListReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyListFilters(_queryDb.PetReadModels.AsNoTracking(), request);

        var total = await filtered.CountAsync(cancellationToken);

        var rows = await filtered
            .OrderBy(x => x.Name)
            .ThenBy(x => x.SpeciesName)
            .ThenBy(x => x.PetId)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(MapListItem).ToList();
        return new PetListReadResult(items, total);
    }

    public async Task<PetTextSearchLookupResult> ResolvePetIdsByTextSearchAsync(
        PetTextSearchLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SearchContainsLikePattern is not { } pattern)
            return new PetTextSearchLookupResult([]);

        var ids = await ApplyAggregateListSearchFilter(
                _queryDb.PetReadModels.AsNoTracking(),
                request.TenantId,
                pattern)
            .OrderBy(x => x.NameNormalized)
            .ThenBy(x => x.PetId)
            .Select(x => x.PetId)
            .ToListAsync(cancellationToken);

        return new PetTextSearchLookupResult(ids);
    }

    public async Task<PetTextFieldsSearchLookupResult> ResolvePetIdsByPetTextFieldsAsync(
        PetTextFieldsSearchLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SearchContainsLikePattern is not { } pattern)
            return new PetTextFieldsSearchLookupResult([]);

        var ids = await ApplyPetTextFieldsSearchFilter(
                _queryDb.PetReadModels.AsNoTracking(),
                request.TenantId,
                pattern)
            .OrderBy(x => x.NameNormalized)
            .ThenBy(x => x.PetId)
            .Select(x => x.PetId)
            .ToListAsync(cancellationToken);

        return new PetTextFieldsSearchLookupResult(ids);
    }

    public async Task<PetIdsByClientIdsLookupResult> ResolvePetIdsByClientIdsAsync(
        PetIdsByClientIdsLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ClientIds.Count == 0)
            return new PetIdsByClientIdsLookupResult([]);

        var ids = await _queryDb.PetReadModels.AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && request.ClientIds.Contains(x.ClientId))
            .OrderBy(x => x.NameNormalized)
            .ThenBy(x => x.PetId)
            .Select(x => x.PetId)
            .ToListAsync(cancellationToken);

        return new PetIdsByClientIdsLookupResult(ids);
    }

    public async Task<PetDisplayLookupResult> GetDisplayByIdsAsync(
        PetDisplayLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PetIds.Count == 0)
            return new PetDisplayLookupResult([]);

        var items = await _queryDb.PetReadModels.AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && request.PetIds.Contains(x.PetId))
            .OrderBy(x => x.NameNormalized)
            .ThenBy(x => x.PetId)
            .Select(x => new PetDisplayLookupItem(
                x.PetId,
                x.ClientId,
                x.Name,
                x.SpeciesId,
                x.SpeciesName))
            .ToListAsync(cancellationToken);

        return new PetDisplayLookupResult(items);
    }

    private static IQueryable<PetReadModel> ApplyListFilters(
        IQueryable<PetReadModel> query,
        PetListReadRequest request)
    {
        query = query.Where(x => x.TenantId == request.TenantId);

        if (request.ClientId is { } clientId)
            query = query.Where(x => x.ClientId == clientId);

        if (request.SpeciesId is { } speciesId)
            query = query.Where(x => x.SpeciesId == speciesId);

        if (request.SearchContainsLikePattern is { } pattern)
            query = ApplyAggregateListSearchFilter(query, request.TenantId, pattern);

        return query;
    }

    /// <summary>
    /// Command DB <see cref="Backend.Veteriner.Application.Pets.Specs.PetsByTenantPagedSpec"/> /
    /// <see cref="Backend.Veteriner.Application.Pets.Specs.PetsByTenantCountSpec"/> ile aynı alan kümesi.
    /// Client metin araması read-model'de <see cref="PetReadModel.ClientFullName"/> üzerinden tek sorguda yapılır.
    /// </summary>
    private static IQueryable<PetReadModel> ApplyAggregateListSearchFilter(
        IQueryable<PetReadModel> query,
        Guid tenantId,
        string pattern)
        => query.Where(x =>
            x.TenantId == tenantId
            && (EF.Functions.Like(x.Name, pattern)
                || (x.Breed != null && EF.Functions.Like(x.Breed, pattern))
                || EF.Functions.Like(x.SpeciesName, pattern)
                || (x.BreedRefName != null && EF.Functions.Like(x.BreedRefName, pattern))
                || EF.Functions.Like(x.ClientFullName, pattern)));

    /// <summary>
    /// <see cref="Backend.Veteriner.Application.Pets.Specs.PetsByTenantTextFieldsSearchSpec"/> ile aynı alan kümesi.
    /// ColorName dahil değildir (command path desteklemez).
    /// </summary>
    private static IQueryable<PetReadModel> ApplyPetTextFieldsSearchFilter(
        IQueryable<PetReadModel> query,
        Guid tenantId,
        string pattern)
        => query.Where(x =>
            x.TenantId == tenantId
            && (EF.Functions.Like(x.Name, pattern)
                || (x.Breed != null && EF.Functions.Like(x.Breed, pattern))
                || EF.Functions.Like(x.SpeciesName, pattern)
                || (x.BreedRefName != null && EF.Functions.Like(x.BreedRefName, pattern))));

    private static PetListItemDto MapListItem(PetReadModel x)
        => new(
            x.PetId,
            x.TenantId,
            x.ClientId,
            x.Name,
            x.SpeciesId,
            x.SpeciesName,
            x.ColorId,
            x.ColorName,
            x.Breed,
            x.Weight ?? 0);
}
