using System.Text.Json;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Clients;

/// <summary>
/// Client integration event'lerini (<c>client.created.v1</c> / <c>client.updated.v1</c>) Query DB
/// <c>ClientReadModels</c> tablosuna project eder.
///
/// Tasarım kararları:
/// - Yalnızca client projection event tiplerini claim eder (<see cref="OutboxMessageQueryFilters.ClientIntegrationEventsOnly"/>);
///   appointment ve generic outbox akışına dokunmaz.
/// - Idempotency: <c>ProcessedProjectionEvents (EventId, ConsumerName)</c> ile dedup; aynı event tekrar uygulanmaz.
/// - Stale/out-of-order koruması: Client event'lerinde per-aggregate sequence yoktur. Bu yüzden ordering
///   anahtarı event'in <c>OccurredAtUtc</c> değeridir; <c>ClientReadModel.LastEventOccurredAtUtc</c>'den
///   daha eski OccurredAtUtc taşıyan event mevcut satırı ezmez. EventId tek başına ordering için kullanılmaz
///   (yalnızca dedup). <c>LastProjectedAtUtc</c> projection wall-clock'tur, event zamanından ayrıdır.
/// </summary>
public sealed class ClientProjectionProcessor : IClientProjectionProcessor
{
    private static readonly SemaphoreSlim BatchGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly ClientProjectionOptions _options;
    private readonly OutboxOptions _outboxOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientProjectionProcessor> _logger;

    public ClientProjectionProcessor(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        IOptions<ClientProjectionOptions> options,
        IOptions<OutboxOptions> outboxOptions,
        TimeProvider timeProvider,
        ILogger<ClientProjectionProcessor> logger)
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
            .ClientIntegrationEventsOnly(_commandDb.OutboxMessages)
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
                        "ClientProjectionDuplicateEventSkipped MessageId={MessageId} EventId={EventId} ConsumerName={ConsumerName}",
                        msg.Id, eventId, _options.ConsumerName);
                }
                else if (applyResult.IsStale)
                {
                    _logger.LogInformation(
                        "ClientProjectionStaleEventSkipped MessageId={MessageId} EventId={EventId} OccurredAtUtc={OccurredAtUtc}",
                        msg.Id, eventId, occurredAtUtc);
                }
            }
            catch (UnknownClientIntegrationEventTypeException ex)
            {
                // Çözülemeyen tip asla başarılı olamaz → retry yapmadan kontrollü dead-letter.
                msg.DeadLetterAtUtc = now;
                msg.RetryCount++;
                msg.LastError = ex.Message;
                msg.Error = ex.ToString();
                await _commandDb.SaveChangesAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "ClientProjectionDeadLetterDetected Type={Type} MessageId={MessageId} Reason=UnknownEventType",
                    msg.Type, msg.Id);
            }
            catch (Exception ex)
            {
                OutboxRetryHelper.ApplyFailure(msg, _outboxOptions, ex);

                if (msg.DeadLetterAtUtc is not null)
                {
                    _logger.LogError(
                        ex,
                        "ClientProjectionDeadLetterDetected Type={Type} MessageId={MessageId} Retry={Retry}",
                        msg.Type, msg.Id, msg.RetryCount);
                }
                else
                {
                    var backoff = OutboxRetryHelper.ComputeBackoff(_outboxOptions.BaseDelaySeconds, msg.RetryCount);
                    _logger.LogWarning(
                        ex,
                        "Client projection retry in {DelaySeconds}s. Type={Type} MessageId={MessageId} Retry={Retry}",
                        (int)backoff.TotalSeconds, msg.Type, msg.Id, msg.RetryCount);
                }

                await _commandDb.SaveChangesAsync(cancellationToken);

                // FIFO korunur: hata alan mesajda dur, sıradaki batch'te tekrar dene.
                break;
            }
        }

        return processedCount;
    }

    private async Task<ClientProjectionApplyResult> ApplyTransactionallyAsync(
        ClientProjectionEvent integrationEvent,
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
            return ClientProjectionApplyResult.DuplicateSkipped();

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var projectedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var inserted = await TryInsertProcessedProjectionEventAsync(eventId, projectedAtUtc, cancellationToken);

            if (!inserted)
            {
                await transaction.RollbackAsync(cancellationToken);
                _queryDb.ChangeTracker.Clear();
                return ClientProjectionApplyResult.DuplicateSkipped();
            }

            var stale = UpsertClientReadModel(integrationEvent.Snapshot, occurredAtUtc, eventId, projectedAtUtc);
            await _queryDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return stale ? ClientProjectionApplyResult.StaleSkipped() : ClientProjectionApplyResult.Applied();
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
    private bool UpsertClientReadModel(
        ClientProjectionSnapshot snap,
        DateTime occurredAtUtc,
        Guid eventId,
        DateTime projectedAtUtc)
    {
        var existing = _queryDb.ClientReadModels.Find(snap.ClientId);

        if (existing is null)
        {
            _queryDb.ClientReadModels.Add(new ClientReadModel
            {
                ClientId = snap.ClientId,
                TenantId = snap.TenantId,
                FullName = snap.FullName,
                FullNameNormalized = snap.FullNameNormalized,
                Email = snap.Email,
                Phone = snap.Phone,
                PhoneNormalized = snap.PhoneNormalized,
                CreatedAtUtc = snap.CreatedAtUtc,
                LastEventId = eventId,
                LastProjectedAtUtc = projectedAtUtc,
                LastEventOccurredAtUtc = occurredAtUtc
            });
            return false;
        }

        // Stale/out-of-order guard: daha eski OccurredAtUtc taşıyan event yeni veriyi ezmemeli.
        // (EventId ordering için kullanılmaz; yalnızca yukarıdaki dedup için.)
        if (occurredAtUtc < existing.LastEventOccurredAtUtc)
            return true;

        existing.TenantId = snap.TenantId;
        existing.FullName = snap.FullName;
        existing.FullNameNormalized = snap.FullNameNormalized;
        existing.Email = snap.Email;
        existing.Phone = snap.Phone;
        existing.PhoneNormalized = snap.PhoneNormalized;
        existing.CreatedAtUtc = snap.CreatedAtUtc;
        existing.LastEventId = eventId;
        existing.LastProjectedAtUtc = projectedAtUtc;
        existing.LastEventOccurredAtUtc = occurredAtUtc;
        return false;
    }

    private static (ClientProjectionEvent Event, Guid EventId, DateTime OccurredAtUtc) DeserializeIntegrationEvent(
        string type, string payload)
    {
        var payloadType = ClientIntegrationEventTypeRegistry.ResolvePayloadType(type);
        var deserialized = JsonSerializer.Deserialize(payload, payloadType, JsonOptions)
            ?? throw new InvalidOperationException($"Client integration event deserialize edilemedi. Type={type}");

        return deserialized switch
        {
            ClientCreatedIntegrationEvent created =>
                (new ClientProjectionEvent(created.Current), created.EventId, created.OccurredAtUtc),
            ClientUpdatedIntegrationEvent updated =>
                (new ClientProjectionEvent(updated.Current), updated.EventId, updated.OccurredAtUtc),
            _ => throw new UnknownClientIntegrationEventTypeException(type)
        };
    }

    /// <summary>Create/Update event'lerini ortak upsert için normalize eden iç taşıyıcı.</summary>
    private readonly record struct ClientProjectionEvent(ClientProjectionSnapshot Snapshot);
}
