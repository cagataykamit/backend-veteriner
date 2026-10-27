using Backend.Veteriner.Api.Observability;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Health;

public sealed class OutboxHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;
    private readonly OutboxHealthOptions _options;
    private readonly OperationsMetrics _metrics;
    private readonly TimeProvider _timeProvider;

    public OutboxHealthCheck(
        AppDbContext db,
        IOptions<OutboxHealthOptions> options,
        OperationsMetrics metrics,
        TimeProvider timeProvider)
    {
        _db = db;
        _options = options.Value;
        _metrics = metrics;
        _timeProvider = timeProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Projeksiyon integration event'lerini generic OutboxProcessor tüketmez; kendi processor ve health kontrolleri vardır.
        // Kapalı projeksiyonların (örn. Pet/Payment) bekleyen olayları burada yaş alarmı üretmemeli.
        var pendingQuery = OutboxMessageQueryFilters
            .ExcludingProjectionIntegrationEvents(_db.OutboxMessages.AsNoTracking())
            .Where(x => x.ProcessedAtUtc == null && x.DeadLetterAtUtc == null);

        var pending = await pendingQuery.CountAsync(cancellationToken);
        var dead = await _db.OutboxMessages.CountAsync(x => x.DeadLetterAtUtc != null && x.ProcessedAtUtc == null, cancellationToken);
        var oldestCreatedAt = pending > 0
            ? await pendingQuery.MinAsync(x => (DateTime?)x.CreatedAtUtc, cancellationToken)
            : null;

        TimeSpan? oldestAge = oldestCreatedAt is { } created
            ? TimeSpan.FromTicks(Math.Max(0, (_timeProvider.GetUtcNow().UtcDateTime - created).Ticks))
            : null;

        var status = new OutboxStatus(pending, dead, oldestAge);
        _metrics.UpdateOutbox(status);

        var evaluation = OutboxHealthEvaluator.Evaluate(status, _options);
        var data = evaluation.Data.Where(kvp => kvp.Value is not null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

        return evaluation.Level switch
        {
            OutboxHealthLevel.Healthy => HealthCheckResult.Healthy(evaluation.Description, data),
            OutboxHealthLevel.Degraded => HealthCheckResult.Degraded(evaluation.Description, data: data),
            _ => HealthCheckResult.Unhealthy(evaluation.Description, data: data)
        };
    }
}
