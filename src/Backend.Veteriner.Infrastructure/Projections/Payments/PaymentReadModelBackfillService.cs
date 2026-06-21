using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Command DB <c>Payments</c> kayıtlarını Query DB <c>PaymentReadModels</c> (list/search read-model) tablosuna
/// idempotent biçimde dolduran backfill servisi (CQRS-14F).
///
/// Tasarım kararları:
/// - <b>Non-destructive upsert</b>: Query tablosu silinmez; eksikler insert, mevcutlar update edilir (PK <c>PaymentId</c>).
/// - <b>Idempotency</b>: tekrar çalıştırma duplicate üretmez; stale guard ile daha yeni gerçek event satırı ezilmez.
/// - <b>Snapshot</b>: list projection ile birebir aynı zenginleştirilmiş snapshot
///   (<see cref="PaymentProjectionSnapshotFactory.Create(Payment, string, string?)"/>) — Command DB'den
///   client/pet isimleri ve normalize alanlar doldurulur.
/// - <b>Timestamp stratejisi</b>: Payment domain'de mutasyon timestamp yok →
///   <see cref="PaymentReadModelBackfillPlanner.BackfillBaselineOccurredAtUtc"/> (UTC MinValue sentinel).
///   <c>LastProjectedAtUtc</c> backfill wall-clock. <c>LastEventId</c> = <see cref="BackfillEventId"/> (Guid.Empty).
/// - <b>ProcessedProjectionEvents'e dokunmaz</b>; bekleyen gerçek event'ler dedup + stale guard ile güvenli uygulanır.
/// - Pet nullable; pet bağlı değilse isim alanları <c>null</c> ile güvenli doldurulur.
/// </summary>
public sealed class PaymentReadModelBackfillService : IPaymentReadModelBackfillService
{
    public const int DefaultBatchSize = 500;

    /// <summary>Backfill kaynaklı satırların <c>LastEventId</c> işareti (gerçek event değil).</summary>
    public static readonly Guid BackfillEventId = Guid.Empty;

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentReadModelBackfillService> _logger;

    public PaymentReadModelBackfillService(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        TimeProvider timeProvider,
        ILogger<PaymentReadModelBackfillService> logger)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PaymentReadModelBackfillResult> BackfillAsync(
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

        var skip = 0;
        while (true)
        {
            var batch = await LoadPaymentBatchAsync(tenantId, skip, batchSize, cancellationToken);
            if (batch.Count == 0)
                break;

            var (batchInserted, batchUpdated, batchSkipped) =
                await UpsertBatchAsync(batch, cancellationToken);

            inserted += batchInserted;
            updated += batchUpdated;
            skippedStale += batchSkipped;

            skip += batch.Count;

            if (batch.Count < batchSize)
                break;
        }

        var queryReadModelCount = await CountQueryReadModelsAsync(tenantId, cancellationToken);
        var parityInSync = commandPaymentCount == queryReadModelCount;

        if (!parityInSync)
        {
            _logger.LogWarning(
                "PaymentReadModelBackfillParityMismatch ScopeTenantId={ScopeTenantId} CommandCount={CommandCount} QueryCount={QueryCount}",
                tenantId, commandPaymentCount, queryReadModelCount);
        }

        var duration = _timeProvider.GetUtcNow().UtcDateTime - started;

        _logger.LogInformation(
            "PaymentReadModelBackfillCompleted ScopeTenantId={ScopeTenantId} Command={CommandCount} Query={QueryCount} Inserted={Inserted} Updated={Updated} SkippedStale={SkippedStale} ParityInSync={ParityInSync} DurationMs={DurationMs}",
            tenantId, commandPaymentCount, queryReadModelCount, inserted, updated, skippedStale, parityInSync,
            (int)duration.TotalMilliseconds);

        return new PaymentReadModelBackfillResult(
            Success: true,
            ScopeTenantId: tenantId,
            CommandPaymentCount: commandPaymentCount,
            QueryReadModelCount: queryReadModelCount,
            InsertedCount: inserted,
            UpdatedCount: updated,
            SkippedStaleCount: skippedStale,
            ParityInSync: parityInSync,
            Duration: duration);
    }

    private async Task<(long Inserted, long Updated, long Skipped)> UpsertBatchAsync(
        IReadOnlyList<PaymentBackfillRow> batch,
        CancellationToken cancellationToken)
    {
        var projectedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var occurredAtUtc = PaymentReadModelBackfillPlanner.ResolveOccurredAtUtc();
        var paymentIds = batch.Select(x => x.Payment.Id).ToList();

        var existingRows = await _queryDb.PaymentReadModels
            .Where(x => paymentIds.Contains(x.PaymentId))
            .ToDictionaryAsync(x => x.PaymentId, cancellationToken);

        long inserted = 0;
        long updated = 0;
        long skipped = 0;

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var row in batch)
            {
                var snapshot = PaymentProjectionSnapshotFactory.Create(
                    row.Payment,
                    row.Client.FullName,
                    row.Pet?.Name);

                existingRows.TryGetValue(row.Payment.Id, out var existing);
                var action = PaymentReadModelBackfillPlanner.Decide(
                    occurredAtUtc, existing?.LastEventOccurredAtUtc);

                switch (action)
                {
                    case PaymentReadModelBackfillAction.Insert:
                        _queryDb.PaymentReadModels.Add(MapToReadModel(snapshot, occurredAtUtc, projectedAtUtc));
                        inserted++;
                        break;

                    case PaymentReadModelBackfillAction.Update:
                        ApplyUpdate(existing!, snapshot, occurredAtUtc, projectedAtUtc);
                        updated++;
                        break;

                    case PaymentReadModelBackfillAction.SkipStale:
                        skipped++;
                        break;
                }
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

        return (inserted, updated, skipped);
    }

    private static PaymentReadModel MapToReadModel(
        PaymentProjectionSnapshot snap,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
    {
        var (clientName, clientNameNormalized, petName, petNameNormalized, notes, notesNormalized) =
            ResolveDenormalizedFields(snap);

        return new PaymentReadModel
        {
            PaymentId = snap.PaymentId,
            TenantId = snap.TenantId,
            ClinicId = snap.ClinicId,
            ClientId = snap.ClientId,
            ClientName = clientName,
            ClientNameNormalized = clientNameNormalized,
            PetId = snap.PetId,
            PetName = petName,
            PetNameNormalized = petNameNormalized,
            Amount = snap.Amount,
            Currency = snap.Currency,
            Method = snap.Method,
            PaidAtUtc = snap.PaidAtUtc,
            Notes = notes,
            NotesNormalized = notesNormalized,
            AppointmentId = snap.AppointmentId,
            ExaminationId = snap.ExaminationId,
            LastEventId = BackfillEventId,
            LastEventOccurredAtUtc = occurredAtUtc,
            LastProjectedAtUtc = projectedAtUtc
        };
    }

    private static void ApplyUpdate(
        PaymentReadModel existing,
        PaymentProjectionSnapshot snap,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
    {
        var (clientName, clientNameNormalized, petName, petNameNormalized, notes, notesNormalized) =
            ResolveDenormalizedFields(snap);

        existing.TenantId = snap.TenantId;
        existing.ClinicId = snap.ClinicId;
        existing.ClientId = snap.ClientId;
        existing.ClientName = clientName;
        existing.ClientNameNormalized = clientNameNormalized;
        existing.PetId = snap.PetId;
        existing.PetName = petName;
        existing.PetNameNormalized = petNameNormalized;
        existing.Amount = snap.Amount;
        existing.Currency = snap.Currency;
        existing.Method = snap.Method;
        existing.PaidAtUtc = snap.PaidAtUtc;
        existing.Notes = notes;
        existing.NotesNormalized = notesNormalized;
        existing.AppointmentId = snap.AppointmentId;
        existing.ExaminationId = snap.ExaminationId;
        existing.LastEventId = BackfillEventId;
        existing.LastEventOccurredAtUtc = occurredAtUtc;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    /// <summary>
    /// Snapshot denormalize alanlarını <see cref="PaymentProjectionProcessor"/> ile birebir aynı kurallarla çözer
    /// (defensive fallback dahil), böylece backfill ve canlı projection aynı değerleri üretir.
    /// </summary>
    private static (string ClientName, string ClientNameNormalized, string? PetName, string? PetNameNormalized,
        string? Notes, string? NotesNormalized) ResolveDenormalizedFields(PaymentProjectionSnapshot snapshot)
    {
        var clientName = string.IsNullOrWhiteSpace(snapshot.ClientName)
            ? string.Empty
            : snapshot.ClientName.Trim();
        var clientNameNormalized = !string.IsNullOrWhiteSpace(snapshot.ClientNameNormalized)
            ? snapshot.ClientNameNormalized
            : NormalizeOptional(clientName) ?? string.Empty;

        var petName = string.IsNullOrWhiteSpace(snapshot.PetName) ? null : snapshot.PetName!.Trim();
        var petNameNormalized = !string.IsNullOrWhiteSpace(snapshot.PetNameNormalized)
            ? snapshot.PetNameNormalized
            : NormalizeOptional(snapshot.PetName);

        var notes = string.IsNullOrWhiteSpace(snapshot.Notes) ? null : snapshot.Notes!.Trim();
        var notesNormalized = !string.IsNullOrWhiteSpace(snapshot.NotesNormalized)
            ? snapshot.NotesNormalized
            : NormalizeOptional(snapshot.Notes);

        return (clientName, clientNameNormalized, petName, petNameNormalized, notes, notesNormalized);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private Task<long> CountCommandPaymentsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = _commandDb.Payments.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(p => p.TenantId == scope);
        return query.LongCountAsync(cancellationToken);
    }

    private Task<long> CountQueryReadModelsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = _queryDb.PaymentReadModels.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(x => x.TenantId == scope);
        return query.LongCountAsync(cancellationToken);
    }

    private async Task<List<PaymentBackfillRow>> LoadPaymentBatchAsync(
        Guid? tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _commandDb.Payments.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(p => p.TenantId == scope);

        var payments = await query
            .OrderBy(p => p.TenantId)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
            return [];

        var clientIds = payments.Select(p => p.ClientId).Distinct().ToList();
        var clients = await _commandDb.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var petIds = payments.Where(p => p.PetId.HasValue).Select(p => p.PetId!.Value).Distinct().ToList();
        var pets = petIds.Count == 0
            ? new Dictionary<Guid, Pet>()
            : await _commandDb.Pets.AsNoTracking()
                .Where(p => petIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

        var rows = new List<PaymentBackfillRow>(payments.Count);
        foreach (var payment in payments)
        {
            if (!clients.TryGetValue(payment.ClientId, out var client))
                throw new InvalidOperationException(
                    $"Payment Client missing. PaymentId={payment.Id} ClientId={payment.ClientId}");

            Pet? pet = null;
            if (payment.PetId is { } petId)
                pets.TryGetValue(petId, out pet);

            rows.Add(new PaymentBackfillRow(payment, client, pet));
        }

        return rows;
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
                "Payment list read-model backfill için Command DB ve Query DB farklı veritabanları olmalıdır.");
        }

        await Task.CompletedTask;
    }

    private sealed record PaymentBackfillRow(Payment Payment, Client Client, Pet? Pet);
}
