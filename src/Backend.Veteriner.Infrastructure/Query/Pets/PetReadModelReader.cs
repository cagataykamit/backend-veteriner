using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Pets;

public sealed class PetReadModelReader : IPetReadModelReader
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
            query = ApplyListSearchFilter(query, pattern);

        return query;
    }

    /// <summary>
    /// Command DB <see cref="Backend.Veteriner.Application.Pets.Specs.PetsByTenantPagedSpec"/> /
    /// <see cref="Backend.Veteriner.Application.Pets.Specs.PetsByTenantCountSpec"/> ile aynı alan kümesi.
    /// Client metin araması read-model'de <see cref="PetReadModel.ClientFullName"/> üzerinden tek sorguda yapılır.
    /// </summary>
    private static IQueryable<PetReadModel> ApplyListSearchFilter(
        IQueryable<PetReadModel> query,
        string pattern)
        => query.Where(x =>
            EF.Functions.Like(x.Name, pattern)
            || (x.Breed != null && EF.Functions.Like(x.Breed, pattern))
            || EF.Functions.Like(x.SpeciesName, pattern)
            || (x.BreedRefName != null && EF.Functions.Like(x.BreedRefName, pattern))
            || EF.Functions.Like(x.ClientFullName, pattern));

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
