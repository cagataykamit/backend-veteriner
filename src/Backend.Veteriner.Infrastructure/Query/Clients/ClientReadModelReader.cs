using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Clients;

public sealed class ClientReadModelReader : IClientReadModelReader, IClientReadModelLookupReader
{
    private readonly QueryDbContext _queryDb;

    public ClientReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<ClientListReadResult> GetListAsync(
        ClientListReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyListFilters(_queryDb.ClientReadModels.AsNoTracking(), request);

        var total = await filtered.CountAsync(cancellationToken);

        var rows = await filtered
            .OrderBy(x => x.FullNameNormalized)
            .ThenBy(x => x.ClientId)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(MapListItem).ToList();
        return new ClientListReadResult(items, total);
    }

    public async Task<ClientTextSearchLookupResult> ResolveClientIdsByTextSearchAsync(
        ClientTextSearchLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SearchContainsLikePattern is not { } pattern)
            return new ClientTextSearchLookupResult([]);

        var ids = await ApplyTextSearchFilter(
                _queryDb.ClientReadModels.AsNoTracking(),
                request.TenantId,
                pattern)
            .OrderBy(x => x.FullNameNormalized)
            .ThenBy(x => x.ClientId)
            .Select(x => x.ClientId)
            .ToListAsync(cancellationToken);

        return new ClientTextSearchLookupResult(ids);
    }

    public async Task<ClientNamesLookupResult> GetNamesByIdsAsync(
        ClientNamesLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ClientIds.Count == 0)
            return new ClientNamesLookupResult([]);

        var items = await _queryDb.ClientReadModels.AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && request.ClientIds.Contains(x.ClientId))
            .OrderBy(x => x.FullNameNormalized)
            .ThenBy(x => x.ClientId)
            .Select(x => new ClientNameLookupItem(x.ClientId, x.FullName))
            .ToListAsync(cancellationToken);

        return new ClientNamesLookupResult(items);
    }

    private static IQueryable<ClientReadModel> ApplyListFilters(
        IQueryable<ClientReadModel> query,
        ClientListReadRequest request)
    {
        query = query.Where(x => x.TenantId == request.TenantId);

        if (request.SearchContainsLikePattern is { } pattern)
            query = ApplyTextSearchFilter(query, request.TenantId, pattern);

        return query;
    }

    /// <summary>
    /// Command DB <see cref="Backend.Veteriner.Application.Clients.Specs.ClientsByTenantPagedSpec"/> /
    /// <see cref="Backend.Veteriner.Application.Clients.Specs.ClientsByTenantCountSpec"/> /
    /// <see cref="Backend.Veteriner.Application.Clients.Specs.ClientsByTenantTextSearchSpec"/> ile aynı alan kümesi.
    /// </summary>
    private static IQueryable<ClientReadModel> ApplyTextSearchFilter(
        IQueryable<ClientReadModel> query,
        Guid tenantId,
        string pattern)
        => query.Where(x =>
            x.TenantId == tenantId
            && (EF.Functions.Like(x.FullName, pattern)
                || (x.Email != null && EF.Functions.Like(x.Email, pattern))
                || (x.Phone != null && EF.Functions.Like(x.Phone, pattern))
                || (x.PhoneNormalized != null && EF.Functions.Like(x.PhoneNormalized, pattern))));

    private static ClientListItemDto MapListItem(ClientReadModel x)
        => new(
            x.ClientId,
            x.TenantId,
            x.CreatedAtUtc,
            x.FullName,
            x.Email,
            x.Phone);
}
