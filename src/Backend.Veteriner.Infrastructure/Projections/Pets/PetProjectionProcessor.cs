using System.Text.Json;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Pets;

/// <summary>
/// Pet integration event'lerini (<c>pet.created.v1</c> / <c>pet.updated.v1</c>) Query DB
/// <c>PetReadModels</c> tablosuna project eder.
///
/// Tasarım kararları:
/// - Yalnızca pet projection event tiplerini claim eder (<see cref="OutboxMessageQueryFilters.PetIntegrationEventsOnly"/>);
///   appointment/client/generic outbox akışına dokunmaz.
/// - Idempotency: <c>ProcessedProjectionEvents (EventId, ConsumerName)</c> ile dedup; aynı event tekrar uygulanmaz.
/// - Stale/out-of-order koruması: Pet event'lerinde per-aggregate sequence yoktur. Ordering anahtarı
///   event'in <c>OccurredAtUtc</c> değeridir; <c>PetReadModel.LastEventOccurredAtUtc</c>'den daha eski
///   OccurredAtUtc taşıyan event mevcut satırı ezmez.
/// - Rename propagation yok: snapshot'taki denormalize Client/Species/Color/BreedRef adları yalnızca
///   pet create/update event'lerinden güncellenir; referans tablo rename'leri bu processor'ı tetiklemez.
/// </summary>
public sealed class PetProjectionProcessor : IPetProjectionProcessor
{
    private static readonly SemaphoreSlim BatchGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly PetProjectionOptions _options;
    private readonly OutboxOptions _outboxOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PetProjectionProcessor> _logger;

    public PetProjectionProcessor(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        IOptions<PetProjectionOptions> options,
        IOptions<OutboxOptions> outboxOptions,
        TimeProvider timeProvider,
        ILogger<PetProjectionProcessor> logger)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
        _options = options.Value;
        _outboxOptions = outboxOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await BatchGate.WaitAsync(cancellationToken);
        try
        {
            return await ProcessFifoBatchAsync(cancellationToken);
        }
        finally
        {
            BatchGate.Release();
        }
    }

    private async Task<int> ProcessFifoBatchAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var batchSize = Math.Max(1, _options.BatchSize);
        var processedCount = 0;

        var batch = await OutboxMessageQueryFilters
            .PetIntegrationEventsOnly(_commandDb.OutboxMessages)
            .Where(m => m.ProcessedAtUtc == null
                     && m.DeadLetterAtUtc == null
                     && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now))
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var msg in batch)
        {
            try
            {
                var (integrationEvent, eventId, occurredAtUtc) = DeserializeIntegrationEvent(msg.Type, msg.Payload);
                var applyResult = await ApplyTransactionallyAsync(
                    integrationEvent, eventId, occurredAtUtc, cancellationToken);

                OutboxRetryHelper.ApplySuccess(msg);
                await _commandDb.SaveChangesAsync(cancellationToken);
                processedCount++;

                if (applyResult.IsDuplicate)
                {
                    _logger.LogInformation(
                        "PetProjectionDuplicateEventSkipped MessageId={MessageId} EventId={EventId} ConsumerName={ConsumerName}",
                        msg.Id, eventId, _options.ConsumerName);
                }
                else if (applyResult.IsStale)
                {
                    _logger.LogInformation(
                        "PetProjectionStaleEventSkipped MessageId={MessageId} EventId={EventId} OccurredAtUtc={OccurredAtUtc}",
                        msg.Id, eventId, occurredAtUtc);
                }
            }
            catch (UnknownPetIntegrationEventTypeException ex)
            {
                msg.DeadLetterAtUtc = now;
                msg.RetryCount++;
                msg.LastError = ex.Message;
                msg.Error = ex.ToString();
                await _commandDb.SaveChangesAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "PetProjectionDeadLetterDetected Type={Type} MessageId={MessageId} Reason=UnknownEventType",
                    msg.Type, msg.Id);
            }
            catch (Exception ex)
            {
                OutboxRetryHelper.ApplyFailure(msg, _outboxOptions, ex);

                if (msg.DeadLetterAtUtc is not null)
                {
                    _logger.LogError(
                        ex,
                        "PetProjectionDeadLetterDetected Type={Type} MessageId={MessageId} Retry={Retry}",
                        msg.Type, msg.Id, msg.RetryCount);
                }
                else
                {
                    var backoff = OutboxRetryHelper.ComputeBackoff(_outboxOptions.BaseDelaySeconds, msg.RetryCount);
                    _logger.LogWarning(
                        ex,
                        "Pet projection retry in {DelaySeconds}s. Type={Type} MessageId={MessageId} Retry={Retry}",
                        (int)backoff.TotalSeconds, msg.Type, msg.Id, msg.RetryCount);
                }

                await _commandDb.SaveChangesAsync(cancellationToken);
                break;
            }
        }

        return processedCount;
    }

    private async Task<PetProjectionApplyResult> ApplyTransactionallyAsync(
        PetProjectionEvent integrationEvent,
        Guid eventId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var fastPathDuplicate = await _queryDb.ProcessedProjectionEvents
            .AsNoTracking()
            .AnyAsync(
                x => x.EventId == eventId && x.ConsumerName == _options.ConsumerName,
                cancellationToken);

        if (fastPathDuplicate)
            return PetProjectionApplyResult.DuplicateSkipped();

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var projectedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var inserted = await TryInsertProcessedProjectionEventAsync(eventId, projectedAtUtc, cancellationToken);

            if (!inserted)
            {
                await transaction.RollbackAsync(cancellationToken);
                _queryDb.ChangeTracker.Clear();
                return PetProjectionApplyResult.DuplicateSkipped();
            }

            var stale = UpsertPetReadModel(integrationEvent.Snapshot, occurredAtUtc, eventId, projectedAtUtc);
            await _queryDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return stale ? PetProjectionApplyResult.StaleSkipped() : PetProjectionApplyResult.Applied();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _queryDb.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<bool> TryInsertProcessedProjectionEventAsync(
        Guid eventId,
        DateTime processedAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ProcessedProjectionEvents (EventId, ConsumerName, ProcessedAtUtc)
            VALUES (@eventId, @consumerName, @processedAtUtc)
            """;

        try
        {
            await _queryDb.Database.ExecuteSqlRawAsync(
                sql,
                [
                    new SqlParameter("@eventId", eventId),
                    new SqlParameter("@consumerName", _options.ConsumerName),
                    new SqlParameter("@processedAtUtc", processedAtUtc)
                ],
                cancellationToken);
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
    }

    /// <returns><c>true</c> ise event stale'dir; read-model verisi korunmuş, yalnızca dedup yazılmıştır.</returns>
    private bool UpsertPetReadModel(
        PetProjectionSnapshot snap,
        DateTime occurredAtUtc,
        Guid eventId,
        DateTime projectedAtUtc)
    {
        var existing = _queryDb.PetReadModels.Find(snap.PetId);

        if (existing is null)
        {
            _queryDb.PetReadModels.Add(MapSnapshotToReadModel(snap, eventId, occurredAtUtc, projectedAtUtc));
            return false;
        }

        if (occurredAtUtc < existing.LastEventOccurredAtUtc)
            return true;

        ApplySnapshotToExisting(existing, snap, eventId, occurredAtUtc, projectedAtUtc);
        return false;
    }

    private static PetReadModel MapSnapshotToReadModel(
        PetProjectionSnapshot snap,
        Guid eventId,
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
            LastEventId = eventId,
            LastEventOccurredAtUtc = occurredAtUtc,
            LastProjectedAtUtc = projectedAtUtc
        };

    private static void ApplySnapshotToExisting(
        PetReadModel existing,
        PetProjectionSnapshot snap,
        Guid eventId,
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
        existing.LastEventId = eventId;
        existing.LastEventOccurredAtUtc = occurredAtUtc;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    private static (PetProjectionEvent Event, Guid EventId, DateTime OccurredAtUtc) DeserializeIntegrationEvent(
        string type, string payload)
    {
        var payloadType = PetIntegrationEventTypeRegistry.ResolvePayloadType(type);
        var deserialized = JsonSerializer.Deserialize(payload, payloadType, JsonOptions)
            ?? throw new InvalidOperationException($"Pet integration event deserialize edilemedi. Type={type}");

        return deserialized switch
        {
            PetCreatedIntegrationEvent created =>
                (new PetProjectionEvent(created.Current), created.EventId, created.OccurredAtUtc),
            PetUpdatedIntegrationEvent updated =>
                (new PetProjectionEvent(updated.Current), updated.EventId, updated.OccurredAtUtc),
            _ => throw new UnknownPetIntegrationEventTypeException(type)
        };
    }

    private readonly record struct PetProjectionEvent(PetProjectionSnapshot Snapshot);
}
