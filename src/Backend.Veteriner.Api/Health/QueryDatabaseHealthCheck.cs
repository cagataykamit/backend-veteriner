using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Backend.Veteriner.Api.Health;

public sealed class QueryDatabaseHealthCheck : IHealthCheck
{
    private readonly IQueryDatabaseStatusReader _statusReader;

    public QueryDatabaseHealthCheck(IQueryDatabaseStatusReader statusReader)
        => _statusReader = statusReader;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _statusReader.GetStatusAsync(cancellationToken);
            if (!status.IsReachable)
                return HealthCheckResult.Unhealthy("Query SQL Server bağlantısı başarısız.");

            if (status.HasPendingMigrations)
                return HealthCheckResult.Unhealthy("Query DB bekleyen migration var.");

            return HealthCheckResult.Healthy("Query SQL Server bağlantısı başarılı.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Query SQL kontrolünde hata.", ex);
        }
    }
}
