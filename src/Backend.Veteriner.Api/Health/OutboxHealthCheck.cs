using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Api.Health;

public sealed class OutboxHealthCheck : IHealthCheck
{
    public static int PendingThreshold = 500; // uyar� e�i�i

    private readonly AppDbContext _db;
    public OutboxHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = await _db.OutboxMessages.CountAsync(x => x.ProcessedAtUtc == null && x.DeadLetterAtUtc == null, cancellationToken);
        var dead = await _db.OutboxMessages.CountAsync(x => x.DeadLetterAtUtc != null && x.ProcessedAtUtc == null, cancellationToken);

        if (dead > 0)
            return HealthCheckResult.Unhealthy($"Dead-letter count: {dead}, pending: {pending}");

        if (pending > PendingThreshold)
            return HealthCheckResult.Degraded($"Pending too high: {pending}");

        return HealthCheckResult.Healthy($"Pending: {pending}, dead: {dead}");
    }
}
