using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Query;

public sealed class QueryDatabaseStatusReader : IQueryDatabaseStatusReader
{
    private readonly QueryDbContext _queryDb;

    public QueryDatabaseStatusReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<QueryDatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _queryDb.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
                return new QueryDatabaseStatus(IsReachable: false, HasPendingMigrations: false);

            var pending = await _queryDb.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingList = pending as IList<string> ?? pending.ToList();
            return new QueryDatabaseStatus(IsReachable: true, HasPendingMigrations: pendingList.Count > 0);
        }
        catch
        {
            return new QueryDatabaseStatus(IsReachable: false, HasPendingMigrations: false);
        }
    }
}
