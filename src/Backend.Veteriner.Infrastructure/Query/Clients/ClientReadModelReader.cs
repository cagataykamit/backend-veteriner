using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Clients;

public sealed class ClientReadModelReader : IClientReadModelReader
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

    private static IQueryable<ClientReadModel> ApplyListFilters(
        IQueryable<ClientReadModel> query,
        ClientListReadRequest request)
    {
        query = query.Where(x => x.TenantId == request.TenantId);

        if (request.SearchContainsLikePattern is { } pattern)
            query = ApplyListSearchFilter(query, pattern);

        return query;
    }

    /// <summary>
    /// Command DB <see cref="Backend.Veteriner.Application.Clients.Specs.ClientsByTenantPagedSpec"/> /
    /// <see cref="Backend.Veteriner.Application.Clients.Specs.ClientsByTenantCountSpec"/> ile aynı alan kümesi.
    /// </summary>
    private static IQueryable<ClientReadModel> ApplyListSearchFilter(
        IQueryable<ClientReadModel> query,
        string pattern)
        => query.Where(x =>
            EF.Functions.Like(x.FullName, pattern)
            || (x.Email != null && EF.Functions.Like(x.Email, pattern))
            || (x.Phone != null && EF.Functions.Like(x.Phone, pattern))
            || (x.PhoneNormalized != null && EF.Functions.Like(x.PhoneNormalized, pattern)));

    private static ClientListItemDto MapListItem(ClientReadModel x)
        => new(
            x.ClientId,
            x.TenantId,
            x.CreatedAtUtc,
            x.FullName,
            x.Email,
            x.Phone);
}
