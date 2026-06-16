using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Backend.Veteriner.Api.Health;

public sealed class QueryDatabaseHealthCheck : IHealthCheck
{
    private readonly QueryDbContext _db;

    public QueryDatabaseHealthCheck(QueryDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
                return HealthCheckResult.Unhealthy("Query SQL Server bağlantısı başarısız.");

            var pending = await _db.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingList = pending as IList<string> ?? pending.ToList();
            if (pendingList.Count > 0)
            {
                return HealthCheckResult.Unhealthy(
                    $"Query DB bekleyen migration var: {string.Join(", ", pendingList)}");
            }

            return HealthCheckResult.Healthy("Query SQL Server bağlantısı başarılı.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Query SQL kontrolünde hata.", ex);
        }
    }
}
