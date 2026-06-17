namespace Backend.Veteriner.Application.Common.Abstractions;

public sealed record QueryDatabaseStatus(
    bool IsReachable,
    bool HasPendingMigrations);

public interface IQueryDatabaseStatusReader
{
    Task<QueryDatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
