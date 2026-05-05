using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Api.Health;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;
    public DatabaseHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("SQL Server bağlantısı başarılı.")
                : HealthCheckResult.Unhealthy("SQL Server bağlantısı başarısız.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL kontrolünde hata.", ex);
        }
    }
}
    