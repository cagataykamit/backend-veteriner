using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Payment finance projection kuyruk durumunu Command DB outbox'tan (yalnızca payment integration event tipleri)
/// ve Query DB erişilebilirlik/migration durumundan derler.
/// </summary>
public sealed class PaymentProjectionStatusReader : IPaymentProjectionStatusReader
{
    private readonly AppDbContext _commandDb;
    private readonly IQueryDatabaseStatusReader _queryDatabaseStatusReader;
    private readonly PaymentProjectionOptions _projectionOptions;
    private readonly TimeProvider _timeProvider;

    public PaymentProjectionStatusReader(
        AppDbContext commandDb,
        IQueryDatabaseStatusReader queryDatabaseStatusReader,
        IOptions<PaymentProjectionOptions> projectionOptions,
        TimeProvider timeProvider)
    {
        _commandDb = commandDb;
        _queryDatabaseStatusReader = queryDatabaseStatusReader;
        _projectionOptions = projectionOptions.Value;
        _timeProvider = timeProvider;
    }

    public async Task<PaymentProjectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var queryStatus = await _queryDatabaseStatusReader.GetStatusAsync(cancellationToken);
        var outboxStats = await GetOutboxStatsAsync(utcNow, cancellationToken);

        TimeSpan? oldestPendingAge = outboxStats.OldestPendingCreatedAtUtc is { } oldest
            ? utcNow - oldest
            : null;

        return new PaymentProjectionStatus(
            outboxStats.PendingCount,
            outboxStats.RetryWaitingCount,
            outboxStats.DeadLetterCount,
            outboxStats.OldestPendingCreatedAtUtc,
            oldestPendingAge,
            outboxStats.NextRetryAtUtc,
            queryStatus.IsReachable,
            queryStatus.HasPendingMigrations,
            _projectionOptions.Enabled);
    }

    private async Task<OutboxStats> GetOutboxStatsAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var stats = await OutboxMessageQueryFilters
            .PaymentIntegrationEventsOnly(_commandDb.OutboxMessages)
            .Where(m => m.ProcessedAtUtc == null)
            .GroupBy(_ => 1)
            .Select(g => new OutboxStats
            {
                DeadLetterCount = g.Count(m => m.DeadLetterAtUtc != null),
                RetryWaitingCount = g.Count(m =>
                    m.DeadLetterAtUtc == null
                    && m.NextAttemptAtUtc != null
                    && m.NextAttemptAtUtc > utcNow),
                PendingCount = g.Count(m =>
                    m.DeadLetterAtUtc == null
                    && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= utcNow)),
                OldestPendingCreatedAtUtc = g
                    .Where(m =>
                        m.DeadLetterAtUtc == null
                        && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= utcNow))
                    .Min(m => (DateTime?)m.CreatedAtUtc),
                NextRetryAtUtc = g
                    .Where(m =>
                        m.DeadLetterAtUtc == null
                        && m.NextAttemptAtUtc != null
                        && m.NextAttemptAtUtc > utcNow)
                    .Min(m => (DateTime?)m.NextAttemptAtUtc)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return stats ?? OutboxStats.Empty;
    }

    private sealed class OutboxStats
    {
        public static OutboxStats Empty { get; } = new();

        public int PendingCount { get; init; }
        public int RetryWaitingCount { get; init; }
        public int DeadLetterCount { get; init; }
        public DateTime? OldestPendingCreatedAtUtc { get; init; }
        public DateTime? NextRetryAtUtc { get; init; }
    }
}
