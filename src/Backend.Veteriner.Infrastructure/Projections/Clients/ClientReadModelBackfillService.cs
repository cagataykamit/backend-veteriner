using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Projections.Clients;

/// <summary>
/// Command DB <c>Clients</c> kayıtlarını Query DB <c>ClientReadModels</c> tablosuna idempotent
/// biçimde dolduran/yeniden oluşturan backfill servisi (CQRS-12B-6).
///
/// Tasarım kararları:
/// - <b>Non-destructive upsert</b>: Appointment rebuild'in aksine Query tablosu silinmez. Mevcut
///   satırlar güvenli şekilde update edilir, eksikler insert edilir. Bu yüzden backfill canlı
///   projection akışıyla aynı anda çalışabilir; bekleyen outbox'ı boşaltma zorunluluğu yoktur.
/// - <b>Idempotency</b>: PK <c>ClientId</c> üzerinden upsert; tekrar çalıştırınca duplicate üretmez.
///   Stale guard sayesinde daha yeni bir event ile yazılmış satır ezilmez
///   (bkz. <see cref="ClientReadModelBackfillPlanner"/>).
/// - <b>Timestamp stratejisi</b>: Backfill bir snapshot'tır, event değildir. Ordering anahtarı
///   (<c>LastEventOccurredAtUtc</c>) için Command DB satırının son mutasyon zamanı kullanılır:
///   <c>UpdatedAtUtc ?? CreatedAtUtc</c> (deterministik). <c>LastProjectedAtUtc</c> backfill
///   wall-clock'tur. <c>LastEventId</c> = <see cref="BackfillEventId"/> (Guid.Empty) ile satırın
///   backfill kaynaklı olduğu işaretlenir.
/// - <b>ProcessedProjectionEvents'e dokunmaz</b>: Backfill sahte event yazmaz. Bekleyen/gelecek
///   gerçek event'ler stale-guard ordering'i sayesinde yine de doğru biçimde uygulanır.
/// - <b>Tenant güvenliği</b>: <paramref name="tenantId"/> verilirse yalnızca o tenant'ın satırları
///   okunup yazılır.
/// </summary>
public sealed class ClientReadModelBackfillService : IClientReadModelBackfillService
{
    public const int DefaultBatchSize = 500;

    /// <summary>Backfill kaynaklı satırların <c>LastEventId</c> işareti (gerçek event değil).</summary>
    public static readonly Guid BackfillEventId = Guid.Empty;

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientReadModelBackfillService> _logger;

    public ClientReadModelBackfillService(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        TimeProvider timeProvider,
        ILogger<ClientReadModelBackfillService> logger)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ClientReadModelBackfillResult> BackfillAsync(
        Guid? tenantId = null,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        var started = _timeProvider.GetUtcNow().UtcDateTime;
        batchSize = Math.Max(1, batchSize);

        await EnsureDistinctDatabasesAsync();

        var commandClientCount = await CountCommandClientsAsync(tenantId, cancellationToken);

        long inserted = 0;
        long updated = 0;
        long skippedStale = 0;

        var skip = 0;
        while (true)
        {
            var batch = await LoadClientBatchAsync(tenantId, skip, batchSize, cancellationToken);
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

        var queryClientCount = await CountQueryClientsAsync(tenantId, cancellationToken);
        var parityInSync = commandClientCount == queryClientCount;

        if (!parityInSync)
        {
            _logger.LogWarning(
                "ClientReadModelBackfillParityMismatch ScopeTenantId={ScopeTenantId} CommandCount={CommandCount} QueryCount={QueryCount}",
                tenantId, commandClientCount, queryClientCount);
        }

        var duration = _timeProvider.GetUtcNow().UtcDateTime - started;

        _logger.LogInformation(
            "ClientReadModelBackfillCompleted ScopeTenantId={ScopeTenantId} Command={CommandCount} Query={QueryCount} Inserted={Inserted} Updated={Updated} SkippedStale={SkippedStale} ParityInSync={ParityInSync} DurationMs={DurationMs}",
            tenantId, commandClientCount, queryClientCount, inserted, updated, skippedStale, parityInSync,
            (int)duration.TotalMilliseconds);

        return new ClientReadModelBackfillResult(
            Success: true,
            ScopeTenantId: tenantId,
            CommandClientCount: commandClientCount,
            QueryClientCount: queryClientCount,
            InsertedCount: inserted,
            UpdatedCount: updated,
            SkippedStaleCount: skippedStale,
            ParityInSync: parityInSync,
            Duration: duration);
    }

    private async Task<(long Inserted, long Updated, long Skipped)> UpsertBatchAsync(
        IReadOnlyList<Client> batch,
        CancellationToken cancellationToken)
    {
        var projectedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var clientIds = batch.Select(c => c.Id).ToList();

        var existingRows = await _queryDb.ClientReadModels
            .Where(x => clientIds.Contains(x.ClientId))
            .ToDictionaryAsync(x => x.ClientId, cancellationToken);

        long inserted = 0;
        long updated = 0;
        long skipped = 0;

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var client in batch)
            {
                var snapshot = ClientProjectionSnapshotFactory.Create(client);
                var occurredAtUtc = ClientReadModelBackfillPlanner.ResolveOccurredAtUtc(
                    client.CreatedAtUtc, client.UpdatedAtUtc);

                existingRows.TryGetValue(client.Id, out var existing);
                var action = ClientReadModelBackfillPlanner.Decide(
                    occurredAtUtc, existing?.LastEventOccurredAtUtc);

                switch (action)
                {
                    case ClientReadModelBackfillAction.Insert:
                        _queryDb.ClientReadModels.Add(MapToReadModel(snapshot, occurredAtUtc, projectedAtUtc));
                        inserted++;
                        break;

                    case ClientReadModelBackfillAction.Update:
                        ApplyUpdate(existing!, snapshot, occurredAtUtc, projectedAtUtc);
                        updated++;
                        break;

                    case ClientReadModelBackfillAction.SkipStale:
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

    private static ClientReadModel MapToReadModel(
        ClientProjectionSnapshot snap,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
        => new()
        {
            ClientId = snap.ClientId,
            TenantId = snap.TenantId,
            FullName = snap.FullName,
            FullNameNormalized = snap.FullNameNormalized,
            Email = snap.Email,
            Phone = snap.Phone,
            PhoneNormalized = snap.PhoneNormalized,
            CreatedAtUtc = snap.CreatedAtUtc,
            LastEventId = BackfillEventId,
            LastProjectedAtUtc = projectedAtUtc,
            LastEventOccurredAtUtc = occurredAtUtc
        };

    private static void ApplyUpdate(
        ClientReadModel existing,
        ClientProjectionSnapshot snap,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
    {
        existing.TenantId = snap.TenantId;
        existing.FullName = snap.FullName;
        existing.FullNameNormalized = snap.FullNameNormalized;
        existing.Email = snap.Email;
        existing.Phone = snap.Phone;
        existing.PhoneNormalized = snap.PhoneNormalized;
        existing.CreatedAtUtc = snap.CreatedAtUtc;
        existing.LastEventId = BackfillEventId;
        existing.LastProjectedAtUtc = projectedAtUtc;
        existing.LastEventOccurredAtUtc = occurredAtUtc;
    }

    private Task<long> CountCommandClientsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = _commandDb.Clients.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(c => c.TenantId == scope);
        return query.LongCountAsync(cancellationToken);
    }

    private Task<long> CountQueryClientsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = _queryDb.ClientReadModels.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(x => x.TenantId == scope);
        return query.LongCountAsync(cancellationToken);
    }

    /// <summary>
    /// Deterministik (TenantId, Id) sıralı batch okuma. Command DB salt okunur; backfill sırasında
    /// yeni client eklenirse sonraki batch'lerde görünebilir (race), bu da güvenlidir: upsert
    /// idempotenttir ve henüz event'i gelmemiş satır için doğru snapshot yazılır.
    /// </summary>
    private Task<List<Client>> LoadClientBatchAsync(
        Guid? tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _commandDb.Clients.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(c => c.TenantId == scope);

        return query
            .OrderBy(c => c.TenantId)
            .ThenBy(c => c.Id)
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
                "Client read-model backfill için Command DB ve Query DB farklı veritabanları olmalıdır.");
        }

        await Task.CompletedTask;
    }
}
