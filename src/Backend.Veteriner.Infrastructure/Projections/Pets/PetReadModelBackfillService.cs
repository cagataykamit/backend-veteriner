using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Projections.Pets;

/// <summary>
/// Command DB <c>Pets</c> kayıtlarını Query DB <c>PetReadModels</c> tablosuna idempotent
/// biçimde dolduran/yeniden oluşturan backfill servisi (CQRS-12C-6).
///
/// Tasarım kararları:
/// - <b>Non-destructive upsert</b>: Query tablosu silinmez; eksikler insert, mevcutlar update edilir.
/// - <b>Idempotency</b>: PK <c>PetId</c> üzerinden upsert; stale guard ile daha yeni gerçek event satırı ezilmez.
/// - <b>Timestamp stratejisi</b>: Pet domain'de mutasyon timestamp yok →
///   <see cref="PetReadModelBackfillPlanner.BackfillBaselineOccurredAtUtc"/> (UTC MinValue sentinel).
///   <c>LastProjectedAtUtc</c> backfill wall-clock. <c>LastEventId</c> = <see cref="BackfillEventId"/>.
/// - <b>ProcessedProjectionEvents'e dokunmaz</b>.
/// </summary>
public sealed class PetReadModelBackfillService : IPetReadModelBackfillService
{
    public const int DefaultBatchSize = 500;

    /// <summary>Backfill kaynaklı satırların <c>LastEventId</c> işareti (gerçek event değil).</summary>
    public static readonly Guid BackfillEventId = Guid.Empty;

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PetReadModelBackfillService> _logger;

    public PetReadModelBackfillService(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        TimeProvider timeProvider,
        ILogger<PetReadModelBackfillService> logger)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PetReadModelBackfillResult> BackfillAsync(
        Guid? tenantId = null,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        var started = _timeProvider.GetUtcNow().UtcDateTime;
        batchSize = Math.Max(1, batchSize);

        await EnsureDistinctDatabasesAsync();

        var commandPetCount = await CountCommandPetsAsync(tenantId, cancellationToken);

        long inserted = 0;
        long updated = 0;
        long skippedStale = 0;

        var skip = 0;
        while (true)
        {
            var batch = await LoadPetBatchAsync(tenantId, skip, batchSize, cancellationToken);
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

        var queryPetCount = await CountQueryPetsAsync(tenantId, cancellationToken);
        var parityInSync = commandPetCount == queryPetCount;

        if (!parityInSync)
        {
            _logger.LogWarning(
                "PetReadModelBackfillParityMismatch ScopeTenantId={ScopeTenantId} CommandCount={CommandCount} QueryCount={QueryCount}",
                tenantId, commandPetCount, queryPetCount);
        }

        var duration = _timeProvider.GetUtcNow().UtcDateTime - started;

        _logger.LogInformation(
            "PetReadModelBackfillCompleted ScopeTenantId={ScopeTenantId} Command={CommandCount} Query={QueryCount} Inserted={Inserted} Updated={Updated} SkippedStale={SkippedStale} ParityInSync={ParityInSync} DurationMs={DurationMs}",
            tenantId, commandPetCount, queryPetCount, inserted, updated, skippedStale, parityInSync,
            (int)duration.TotalMilliseconds);

        return new PetReadModelBackfillResult(
            Success: true,
            ScopeTenantId: tenantId,
            CommandPetCount: commandPetCount,
            QueryPetCount: queryPetCount,
            InsertedCount: inserted,
            UpdatedCount: updated,
            SkippedStaleCount: skippedStale,
            ParityInSync: parityInSync,
            Duration: duration);
    }

    private async Task<(long Inserted, long Updated, long Skipped)> UpsertBatchAsync(
        IReadOnlyList<PetBackfillRow> batch,
        CancellationToken cancellationToken)
    {
        var projectedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var occurredAtUtc = PetReadModelBackfillPlanner.ResolveOccurredAtUtc();
        var petIds = batch.Select(x => x.Pet.Id).ToList();

        var existingRows = await _queryDb.PetReadModels
            .Where(x => petIds.Contains(x.PetId))
            .ToDictionaryAsync(x => x.PetId, cancellationToken);

        long inserted = 0;
        long updated = 0;
        long skipped = 0;

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var row in batch)
            {
                var snapshot = PetProjectionSnapshotFactory.Create(
                    row.Pet,
                    row.Client,
                    row.Pet.Species!,
                    row.Pet.BreedRef,
                    row.Pet.ColorRef);

                existingRows.TryGetValue(row.Pet.Id, out var existing);
                var action = PetReadModelBackfillPlanner.Decide(
                    occurredAtUtc, existing?.LastEventOccurredAtUtc);

                switch (action)
                {
                    case PetReadModelBackfillAction.Insert:
                        _queryDb.PetReadModels.Add(MapToReadModel(snapshot, occurredAtUtc, projectedAtUtc));
                        inserted++;
                        break;

                    case PetReadModelBackfillAction.Update:
                        ApplyUpdate(existing!, snapshot, occurredAtUtc, projectedAtUtc);
                        updated++;
                        break;

                    case PetReadModelBackfillAction.SkipStale:
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

    private static PetReadModel MapToReadModel(
        PetProjectionSnapshot snap,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
        => new()
        {
            PetId = snap.PetId,
            TenantId = snap.TenantId,
            ClientId = snap.ClientId,
            ClientFullName = snap.ClientFullName,
            ClientFullNameNormalized = snap.ClientFullNameNormalized,
            Name = snap.Name,
            NameNormalized = snap.NameNormalized,
            SpeciesId = snap.SpeciesId,
            SpeciesName = snap.SpeciesName,
            SpeciesNameNormalized = snap.SpeciesNameNormalized,
            BreedId = snap.BreedId,
            Breed = snap.Breed,
            BreedRefName = snap.BreedRefName,
            ColorId = snap.ColorId,
            ColorName = snap.ColorName,
            ColorNameNormalized = snap.ColorNameNormalized,
            Gender = snap.Gender,
            BirthDate = snap.BirthDate,
            Weight = snap.Weight,
            LastEventId = BackfillEventId,
            LastProjectedAtUtc = projectedAtUtc,
            LastEventOccurredAtUtc = occurredAtUtc
        };

    private static void ApplyUpdate(
        PetReadModel existing,
        PetProjectionSnapshot snap,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
    {
        existing.TenantId = snap.TenantId;
        existing.ClientId = snap.ClientId;
        existing.ClientFullName = snap.ClientFullName;
        existing.ClientFullNameNormalized = snap.ClientFullNameNormalized;
        existing.Name = snap.Name;
        existing.NameNormalized = snap.NameNormalized;
        existing.SpeciesId = snap.SpeciesId;
        existing.SpeciesName = snap.SpeciesName;
        existing.SpeciesNameNormalized = snap.SpeciesNameNormalized;
        existing.BreedId = snap.BreedId;
        existing.Breed = snap.Breed;
        existing.BreedRefName = snap.BreedRefName;
        existing.ColorId = snap.ColorId;
        existing.ColorName = snap.ColorName;
        existing.ColorNameNormalized = snap.ColorNameNormalized;
        existing.Gender = snap.Gender;
        existing.BirthDate = snap.BirthDate;
        existing.Weight = snap.Weight;
        existing.LastEventId = BackfillEventId;
        existing.LastProjectedAtUtc = projectedAtUtc;
        existing.LastEventOccurredAtUtc = occurredAtUtc;
    }

    private Task<long> CountCommandPetsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = _commandDb.Pets.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(p => p.TenantId == scope);
        return query.LongCountAsync(cancellationToken);
    }

    private Task<long> CountQueryPetsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = _queryDb.PetReadModels.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(x => x.TenantId == scope);
        return query.LongCountAsync(cancellationToken);
    }

    private async Task<List<PetBackfillRow>> LoadPetBatchAsync(
        Guid? tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _commandDb.Pets.AsNoTracking();
        if (tenantId is { } scope)
            query = query.Where(p => p.TenantId == scope);

        var pets = await query
            .Include(p => p.Species)
            .Include(p => p.BreedRef)
            .Include(p => p.ColorRef)
            .OrderBy(p => p.TenantId)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (pets.Count == 0)
            return [];

        var clientIds = pets.Select(p => p.ClientId).Distinct().ToList();
        var clients = await _commandDb.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var rows = new List<PetBackfillRow>(pets.Count);
        foreach (var pet in pets)
        {
            if (pet.Species is null)
                throw new InvalidOperationException($"Pet Species navigation missing. PetId={pet.Id}");

            if (!clients.TryGetValue(pet.ClientId, out var client))
                throw new InvalidOperationException($"Pet Client missing. PetId={pet.Id} ClientId={pet.ClientId}");

            rows.Add(new PetBackfillRow(pet, client));
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
                "Pet read-model backfill için Command DB ve Query DB farklı veritabanları olmalıdır.");
        }

        await Task.CompletedTask;
    }

    private sealed record PetBackfillRow(Pet Pet, Client Client);
}
