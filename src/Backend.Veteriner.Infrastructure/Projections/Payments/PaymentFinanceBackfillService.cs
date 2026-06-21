using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Command DB <c>Payments</c> kayıtlarından Query DB <c>PaymentDailyContributionReadModel</c> +
/// <c>ClinicDailyPaymentStatsReadModel</c> tablolarını idempotent biçimde dolduran backfill servisi (CQRS-13D).
///
/// Tasarım kararları:
/// - Non-destructive upsert; projection processor ile aynı recompute kuralları (increment yok).
/// - Stale guard: canlı projection event'i backfill snapshot'ından yeniyse satır korunur.
/// - <c>ProcessedProjectionEvents</c>'e dokunmaz; bekleyen event'ler dedup + stale guard ile güvenli uygulanır.
/// - Ordering anahtarı: <see cref="PaymentFinanceBackfillPlanner.BackfillBaselineOccurredAtUtc"/> (Payment'ta timestamp yok).
/// </summary>
public sealed class PaymentFinanceBackfillService : IPaymentFinanceBackfillService
{
    public const int DefaultBatchSize = 500;

    public static readonly Guid BackfillEventId = Guid.Empty;

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly IPaymentFinanceParityReader _parityReader;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentFinanceBackfillService> _logger;

    public PaymentFinanceBackfillService(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        IPaymentFinanceParityReader parityReader,
        TimeProvider timeProvider,
        ILogger<PaymentFinanceBackfillService> logger)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
        _parityReader = parityReader;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PaymentFinanceBackfillResult> BackfillAsync(
        Guid? tenantId = null,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        var started = _timeProvider.GetUtcNow().UtcDateTime;
        batchSize = Math.Max(1, batchSize);

        await EnsureDistinctDatabasesAsync();

        var commandPaymentCount = await CountCommandPaymentsAsync(tenantId, cancellationToken);

        long inserted = 0;
        long updated = 0;
        long skippedStale = 0;
        long recomputedBuckets = 0;

        var skip = 0;
        while (true)
        {
            var batch = await LoadPaymentBatchAsync(tenantId, skip, batchSize, cancellationToken);
            if (batch.Count == 0)
                break;

            var (batchInserted, batchUpdated, batchSkipped, batchBuckets) =
                await UpsertBatchAsync(batch, cancellationToken);

            inserted += batchInserted;
            updated += batchUpdated;
            skippedStale += batchSkipped;
            recomputedBuckets += batchBuckets;

            skip += batch.Count;

            if (batch.Count < batchSize)
                break;
        }

        var parity = tenantId is { } scope
            ? await _parityReader.GetTenantParityAsync(scope, cancellationToken)
            : await _parityReader.GetGlobalParityAsync(cancellationToken);

        if (!parity.InSync)
        {
            _logger.LogWarning(
                "PaymentFinanceBackfillParityMismatch ScopeTenantId={ScopeTenantId} CommandCount={CommandCount} QueryContributionCount={QueryContributionCount} DailyBucketMismatchCount={DailyBucketMismatchCount}",
                tenantId,
                parity.CommandPaymentCount,
                parity.QueryContributionCount,
                parity.DailyBucketMismatchCount);
        }

        var duration = _timeProvider.GetUtcNow().UtcDateTime - started;

        _logger.LogInformation(
            "PaymentFinanceBackfillCompleted ScopeTenantId={ScopeTenantId} Command={CommandCount} QueryContribution={QueryContributionCount} Inserted={Inserted} Updated={Updated} SkippedStale={SkippedStale} RecomputedBuckets={RecomputedBuckets} CountParityInSync={CountParityInSync} DailyBucketParityInSync={DailyBucketParityInSync} DurationMs={DurationMs}",
            tenantId,
            parity.CommandPaymentCount,
            parity.QueryContributionCount,
            inserted,
            updated,
            skippedStale,
            recomputedBuckets,
            parity.CountInSync,
            parity.DailyBucketParityInSync,
            (int)duration.TotalMilliseconds);

        return new PaymentFinanceBackfillResult(
            Success: true,
            ScopeTenantId: tenantId,
            CommandPaymentCount: parity.CommandPaymentCount,
            QueryContributionCount: parity.QueryContributionCount,
            InsertedCount: inserted,
            UpdatedCount: updated,
            SkippedStaleCount: skippedStale,
            RecomputedBucketCount: recomputedBuckets,
            CountParityInSync: parity.CountInSync,
            DailyBucketParityInSync: parity.DailyBucketParityInSync,
            Duration: duration);
    }

    private async Task<(long Inserted, long Updated, long Skipped, long RecomputedBuckets)> UpsertBatchAsync(
        IReadOnlyList<Payment> batch,
        CancellationToken cancellationToken)
    {
        var projectedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var backfillOccurredAtUtc = PaymentFinanceBackfillPlanner.ResolveOccurredAtUtc();
        var paymentIds = batch.Select(p => p.Id).ToList();

        var existingRows = await _queryDb.PaymentDailyContributionReadModels
            .Where(x => paymentIds.Contains(x.PaymentId))
            .ToDictionaryAsync(x => x.PaymentId, cancellationToken);

        long inserted = 0;
        long updated = 0;
        long skipped = 0;
        var affectedBuckets = new HashSet<DailyBucketKey>();

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var payment in batch)
            {
                var snapshot = PaymentProjectionSnapshotFactory.Create(payment);
                existingRows.TryGetValue(payment.Id, out var existing);
                var action = PaymentFinanceBackfillPlanner.Decide(
                    backfillOccurredAtUtc,
                    existing?.LastEventOccurredAtUtc);

                DailyBucketKey? oldBucket = existing is not null
                    ? new DailyBucketKey(existing.TenantId, existing.ClinicId, existing.LocalDate, existing.Currency)
                    : null;

                var newLocalDate = OperationDayBounds.ToLocalDate(snapshot.PaidAtUtc);
                var newBucket = new DailyBucketKey(snapshot.TenantId, snapshot.ClinicId, newLocalDate, snapshot.Currency);

                switch (action)
                {
                    case PaymentFinanceBackfillAction.Insert:
                        _queryDb.PaymentDailyContributionReadModels.Add(MapContribution(
                            snapshot, newLocalDate, backfillOccurredAtUtc, projectedAtUtc));
                        affectedBuckets.Add(newBucket);
                        inserted++;
                        break;

                    case PaymentFinanceBackfillAction.Update:
                        if (oldBucket is not null)
                            affectedBuckets.Add(oldBucket.Value);
                        ApplyContributionUpdate(existing!, snapshot, newLocalDate, backfillOccurredAtUtc, projectedAtUtc);
                        affectedBuckets.Add(newBucket);
                        updated++;
                        break;

                    case PaymentFinanceBackfillAction.SkipStale:
                        skipped++;
                        break;
                }
            }

            await _queryDb.SaveChangesAsync(cancellationToken);

            foreach (var bucket in affectedBuckets)
            {
                RecalculateDailyStats(
                    bucket.TenantId,
                    bucket.ClinicId,
                    bucket.LocalDate,
                    bucket.Currency,
                    BackfillEventId,
                    backfillOccurredAtUtc,
                    projectedAtUtc);
            }

            await _queryDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _queryDb.ChangeTracker.Clear();
        }

        return (inserted, updated, skipped, affectedBuckets.Count);
    }

    private static PaymentDailyContributionReadModel MapContribution(
        PaymentProjectionSnapshot snap,
        DateOnly localDate,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
        => new()
        {
            PaymentId = snap.PaymentId,
            TenantId = snap.TenantId,
            ClinicId = snap.ClinicId,
            LocalDate = localDate,
            Currency = snap.Currency,
            Amount = snap.Amount,
            LastEventId = BackfillEventId,
            LastEventOccurredAtUtc = occurredAtUtc,
            LastProjectedAtUtc = projectedAtUtc
        };

    private static void ApplyContributionUpdate(
        PaymentDailyContributionReadModel existing,
        PaymentProjectionSnapshot snap,
        DateOnly localDate,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
    {
        existing.TenantId = snap.TenantId;
        existing.ClinicId = snap.ClinicId;
        existing.LocalDate = localDate;
        existing.Currency = snap.Currency;
        existing.Amount = snap.Amount;
        existing.LastEventId = BackfillEventId;
        existing.LastEventOccurredAtUtc = occurredAtUtc;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    private void RecalculateDailyStats(
        Guid tenantId,
        Guid clinicId,
        DateOnly localDate,
        string currency,
        Guid eventId,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
    {
        var contributions = _queryDb.PaymentDailyContributionReadModels
            .AsNoTracking()
            .Where(c =>
                c.TenantId == tenantId
                && c.ClinicId == clinicId
                && c.LocalDate == localDate
                && c.Currency == currency)
            .ToList();

        var paidCount = contributions.Count;
        var paidTotalAmount = contributions.Sum(c => c.Amount);

        var existing = _queryDb.ClinicDailyPaymentStatsReadModels.Find(tenantId, clinicId, localDate, currency);

        if (paidCount == 0)
        {
            if (existing is not null)
                _queryDb.ClinicDailyPaymentStatsReadModels.Remove(existing);
            return;
        }

        if (existing is null)
        {
            _queryDb.ClinicDailyPaymentStatsReadModels.Add(new ClinicDailyPaymentStatsReadModel
            {
                TenantId = tenantId,
                ClinicId = clinicId,
                LocalDate = localDate,
                Currency = currency,
                PaidTotalAmount = paidTotalAmount,
                PaidCount = paidCount,
                LastEventId = eventId,
                LastEventOccurredAtUtc = occurredAtUtc,
                LastProjectedAtUtc = projectedAtUtc
            });
            return;
        }

        existing.PaidTotalAmount = paidTotalAmount;
        existing.PaidCount = paidCount;
        existing.LastEventId = eventId;
        existing.LastEventOccurredAtUtc = occurredAtUtc;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    private Task<long> CountCommandPaymentsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = _commandDb.Payments.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(p => p.TenantId == scope);
        return query.LongCountAsync(cancellationToken);
    }

    private Task<List<Payment>> LoadPaymentBatchAsync(
        Guid? tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _commandDb.Payments.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(p => p.TenantId == scope);

        return query
            .OrderBy(p => p.TenantId)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureDistinctDatabasesAsync()
    {
        var commandConnection = _commandDb.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Command DB connection string bulunamadı.");
        var queryConnection = _queryDb.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Query DB connection string bulunamadı.");

        var commandCatalog = new SqlConnectionStringBuilder(commandConnection).InitialCatalog;
        var queryCatalog = new SqlConnectionStringBuilder(queryConnection).InitialCatalog;

        if (string.Equals(commandCatalog, queryCatalog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Payment finance backfill için Command DB ve Query DB farklı veritabanları olmalıdır.");
        }

        await Task.CompletedTask;
    }

    private readonly record struct DailyBucketKey(Guid TenantId, Guid ClinicId, DateOnly LocalDate, string Currency);
}
