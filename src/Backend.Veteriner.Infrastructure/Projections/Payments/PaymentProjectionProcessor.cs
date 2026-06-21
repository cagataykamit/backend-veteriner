using System.Text.Json;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Payment integration event'lerini (<c>payment.created.v1</c> / <c>payment.updated.v1</c>) Query DB finance
/// read-model'lerine project eder.
///
/// Tasarım kararları:
/// - Per-payment <see cref="PaymentDailyContributionReadModel"/> eski bucket kaynağıdır (event'te Previous yok).
/// - Günlük aggregate (<see cref="ClinicDailyPaymentStatsReadModel"/>) increment değil recompute ile güncellenir.
/// - Idempotency: <c>ProcessedProjectionEvents (EventId, ConsumerName)</c>.
/// - Stale guard: contribution <c>LastEventOccurredAtUtc</c> üzerinden.
/// </summary>
public sealed class PaymentProjectionProcessor : IPaymentProjectionProcessor
{
    private static readonly SemaphoreSlim BatchGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly PaymentProjectionOptions _options;
    private readonly OutboxOptions _outboxOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentProjectionProcessor> _logger;
    private readonly IPaymentOutboxClaimRepository _claimRepository;
    private readonly IPaymentProjectionWorkerIdentity _workerIdentity;

    public PaymentProjectionProcessor(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        IOptions<PaymentProjectionOptions> options,
        IOptions<OutboxOptions> outboxOptions,
        TimeProvider timeProvider,
        ILogger<PaymentProjectionProcessor> logger,
        IPaymentOutboxClaimRepository claimRepository,
        IPaymentProjectionWorkerIdentity workerIdentity)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
        _options = options.Value;
        _outboxOptions = outboxOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _claimRepository = claimRepository;
        _workerIdentity = workerIdentity;
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await BatchGate.WaitAsync(cancellationToken);
        try
        {
            return _options.ClaimingEnabled
                ? await ProcessClaimBatchAsync(cancellationToken)
                : await ProcessFifoBatchAsync(cancellationToken);
        }
        finally
        {
            BatchGate.Release();
        }
    }

    private async Task<int> ProcessClaimBatchAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var workerId = _workerIdentity.WorkerId;
        var batchSize = Math.Max(1, _options.ClaimBatchSize);
        var leaseDuration = TimeSpan.FromSeconds(_options.LeaseDurationSeconds);
        var processedCount = 0;

        _logger.LogInformation(
            "PaymentProjectionClaimBatchStarted WorkerId={WorkerId} BatchSize={BatchSize} LeaseDurationSeconds={LeaseDurationSeconds}",
            workerId,
            batchSize,
            _options.LeaseDurationSeconds);

        var claimed = await _claimRepository.ClaimNextBatchAsync(
            workerId,
            batchSize,
            leaseDuration,
            cancellationToken);

        foreach (var claimedMessage in claimed)
        {
            try
            {
                var (snapshot, eventId, occurredAtUtc) =
                    DeserializeIntegrationEvent(claimedMessage.Type, claimedMessage.Payload);
                var applyResult = await ApplyTransactionallyAsync(
                    snapshot, eventId, occurredAtUtc, cancellationToken);

                if (applyResult.IsDuplicate)
                {
                    _logger.LogInformation(
                        "PaymentProjectionDuplicateEventSkipped MessageId={MessageId} EventId={EventId} ConsumerName={ConsumerName}",
                        claimedMessage.Id, eventId, _options.ConsumerName);
                }
                else if (applyResult.IsStale)
                {
                    _logger.LogInformation(
                        "PaymentProjectionStaleEventSkipped MessageId={MessageId} EventId={EventId} OccurredAtUtc={OccurredAtUtc}",
                        claimedMessage.Id, eventId, occurredAtUtc);
                }

                var acknowledged = await _claimRepository.MarkProcessedAsync(
                    claimedMessage.Id,
                    claimedMessage.ClaimToken,
                    workerId,
                    cancellationToken);

                if (!acknowledged)
                {
                    _logger.LogWarning(
                        "PaymentProjectionStaleCompletionRejected MessageId={MessageId} EventId={EventId}",
                        claimedMessage.Id,
                        eventId);
                }

                processedCount++;
            }
            catch (UnknownPaymentIntegrationEventTypeException ex)
            {
                var deadLettered = await _claimRepository.MarkDeadLetterAsync(
                    claimedMessage.Id,
                    claimedMessage.ClaimToken,
                    workerId,
                    ex.Message,
                    cancellationToken);

                if (!deadLettered)
                {
                    _logger.LogWarning(
                        "PaymentProjectionClaimDeadLetterRejected MessageId={MessageId}",
                        claimedMessage.Id);
                }
                else
                {
                    _logger.LogError(
                        ex,
                        "PaymentProjectionDeadLetterDetected Type={Type} Reason=UnknownEventType",
                        claimedMessage.Type);
                }
            }
            catch (Exception ex)
            {
                var newRetryCount = claimedMessage.RetryCount + 1;
                if (newRetryCount >= _outboxOptions.MaxRetryCount)
                {
                    var deadLettered = await _claimRepository.MarkDeadLetterAsync(
                        claimedMessage.Id,
                        claimedMessage.ClaimToken,
                        workerId,
                        ex.Message,
                        cancellationToken);

                    if (!deadLettered)
                    {
                        _logger.LogWarning(
                            "PaymentProjectionClaimDeadLetterRejected MessageId={MessageId}",
                            claimedMessage.Id);
                    }
                    else
                    {
                        _logger.LogError(
                            ex,
                            "PaymentProjectionDeadLetterDetected Type={Type} Retry={Retry}",
                            claimedMessage.Type,
                            newRetryCount);
                    }
                }
                else
                {
                    var backoff = OutboxRetryHelper.ComputeBackoff(_outboxOptions.BaseDelaySeconds, newRetryCount);
                    var nextAttemptAtUtc = now.Add(backoff);
                    var retried = await _claimRepository.MarkRetryAsync(
                        claimedMessage.Id,
                        claimedMessage.ClaimToken,
                        workerId,
                        newRetryCount,
                        nextAttemptAtUtc,
                        ex.Message,
                        cancellationToken);

                    if (!retried)
                    {
                        _logger.LogWarning(
                            "PaymentProjectionClaimRetryRejected MessageId={MessageId}",
                            claimedMessage.Id);
                    }
                    else
                    {
                        _logger.LogWarning(
                            ex,
                            "Payment projection retry in {DelaySeconds}s. Type={Type} Retry={Retry}",
                            (int)backoff.TotalSeconds,
                            claimedMessage.Type,
                            newRetryCount);
                    }
                }

                break;
            }
        }

        if (processedCount > 0 || claimed.Count > 0)
        {
            _logger.LogInformation(
                "PaymentProjectionClaimBatchCompleted WorkerId={WorkerId} ClaimedCount={ClaimedCount} ProcessedCount={ProcessedCount} ConsumerName={ConsumerName}",
                workerId,
                claimed.Count,
                processedCount,
                _options.ConsumerName);
        }

        return processedCount;
    }

    private async Task<int> ProcessFifoBatchAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var batchSize = Math.Max(1, _options.BatchSize);
        var processedCount = 0;

        var batch = await OutboxMessageQueryFilters
            .PaymentIntegrationEventsOnly(_commandDb.OutboxMessages)
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
                var (snapshot, eventId, occurredAtUtc) = DeserializeIntegrationEvent(msg.Type, msg.Payload);
                var applyResult = await ApplyTransactionallyAsync(
                    snapshot, eventId, occurredAtUtc, cancellationToken);

                OutboxRetryHelper.ApplySuccess(msg);
                await _commandDb.SaveChangesAsync(cancellationToken);
                processedCount++;

                if (applyResult.IsDuplicate)
                {
                    _logger.LogInformation(
                        "PaymentProjectionDuplicateEventSkipped MessageId={MessageId} EventId={EventId} ConsumerName={ConsumerName}",
                        msg.Id, eventId, _options.ConsumerName);
                }
                else if (applyResult.IsStale)
                {
                    _logger.LogInformation(
                        "PaymentProjectionStaleEventSkipped MessageId={MessageId} EventId={EventId} OccurredAtUtc={OccurredAtUtc}",
                        msg.Id, eventId, occurredAtUtc);
                }
            }
            catch (UnknownPaymentIntegrationEventTypeException ex)
            {
                msg.DeadLetterAtUtc = now;
                msg.RetryCount++;
                msg.LastError = ex.Message;
                msg.Error = ex.ToString();
                await _commandDb.SaveChangesAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "PaymentProjectionDeadLetterDetected Type={Type} MessageId={MessageId} Reason=UnknownEventType",
                    msg.Type, msg.Id);
            }
            catch (Exception ex)
            {
                OutboxRetryHelper.ApplyFailure(msg, _outboxOptions, ex);

                if (msg.DeadLetterAtUtc is not null)
                {
                    _logger.LogError(
                        ex,
                        "PaymentProjectionDeadLetterDetected Type={Type} MessageId={MessageId} Retry={Retry}",
                        msg.Type, msg.Id, msg.RetryCount);
                }
                else
                {
                    var backoff = OutboxRetryHelper.ComputeBackoff(_outboxOptions.BaseDelaySeconds, msg.RetryCount);
                    _logger.LogWarning(
                        ex,
                        "Payment projection retry in {DelaySeconds}s. Type={Type} MessageId={MessageId} Retry={Retry}",
                        (int)backoff.TotalSeconds, msg.Type, msg.Id, msg.RetryCount);
                }

                await _commandDb.SaveChangesAsync(cancellationToken);
                break;
            }
        }

        return processedCount;
    }

    private async Task<PaymentProjectionApplyResult> ApplyTransactionallyAsync(
        PaymentProjectionSnapshot snapshot,
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
            return PaymentProjectionApplyResult.DuplicateSkipped();

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var projectedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var inserted = await TryInsertProcessedProjectionEventAsync(eventId, projectedAtUtc, cancellationToken);

            if (!inserted)
            {
                await transaction.RollbackAsync(cancellationToken);
                _queryDb.ChangeTracker.Clear();
                return PaymentProjectionApplyResult.DuplicateSkipped();
            }

            var stale = await ApplySnapshotChangeAsync(snapshot, occurredAtUtc, eventId, projectedAtUtc, cancellationToken);

            // Aynı transaction içinde list read-model upsert. Finance stale ise read-model de korunur
            // (tek ordering otoritesi: contribution LastEventOccurredAtUtc).
            if (!stale)
                ApplyReadModelChange(snapshot, eventId, occurredAtUtc, projectedAtUtc);

            await _queryDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return stale ? PaymentProjectionApplyResult.StaleSkipped() : PaymentProjectionApplyResult.Applied();
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

    /// <returns><c>true</c> ise event stale'dir; contribution ve daily aggregate korunmuştur.</returns>
    private async Task<bool> ApplySnapshotChangeAsync(
        PaymentProjectionSnapshot snapshot,
        DateTime occurredAtUtc,
        Guid eventId,
        DateTime projectedAtUtc,
        CancellationToken cancellationToken)
    {
        var existing = _queryDb.PaymentDailyContributionReadModels.Find(snapshot.PaymentId);
        DailyBucketKey? oldBucket = null;

        if (existing is not null)
        {
            oldBucket = new DailyBucketKey(existing.TenantId, existing.ClinicId, existing.LocalDate, existing.Currency);

            if (occurredAtUtc < existing.LastEventOccurredAtUtc)
                return true;
        }

        var newLocalDate = OperationDayBounds.ToLocalDate(snapshot.PaidAtUtc);
        var newBucket = new DailyBucketKey(snapshot.TenantId, snapshot.ClinicId, newLocalDate, snapshot.Currency);

        if (existing is null)
        {
            _queryDb.PaymentDailyContributionReadModels.Add(new PaymentDailyContributionReadModel
            {
                PaymentId = snapshot.PaymentId,
                TenantId = snapshot.TenantId,
                ClinicId = snapshot.ClinicId,
                LocalDate = newLocalDate,
                Currency = snapshot.Currency,
                Amount = snapshot.Amount,
                LastEventId = eventId,
                LastEventOccurredAtUtc = occurredAtUtc,
                LastProjectedAtUtc = projectedAtUtc
            });
        }
        else
        {
            existing.TenantId = snapshot.TenantId;
            existing.ClinicId = snapshot.ClinicId;
            existing.LocalDate = newLocalDate;
            existing.Currency = snapshot.Currency;
            existing.Amount = snapshot.Amount;
            existing.LastEventId = eventId;
            existing.LastEventOccurredAtUtc = occurredAtUtc;
            existing.LastProjectedAtUtc = projectedAtUtc;
        }

        // Contribution satırı recompute SUM sorgusunda görünsün (Appointment deseni).
        await _queryDb.SaveChangesAsync(cancellationToken);

        var affectedBuckets = new HashSet<DailyBucketKey> { newBucket };
        if (oldBucket is not null)
            affectedBuckets.Add(oldBucket.Value);

        foreach (var bucket in affectedBuckets)
        {
            RecalculateDailyStats(
                bucket.TenantId,
                bucket.ClinicId,
                bucket.LocalDate,
                bucket.Currency,
                eventId,
                occurredAtUtc,
                projectedAtUtc);
        }

        return false;
    }

    /// <summary>
    /// PaymentReadModel list/search satırını idempotent upsert eder (aynı Query DB transaction'ı içinde).
    /// Create → insert, Update → overwrite. Enrichment alanları (ClientName/PetName/Notes) snapshot'tan gelir;
    /// 14C öncesi (eski) payload'larda bu alanlar yoksa defensive fallback uygulanır:
    /// ClientName/ClientNameNormalized -> boş string, PetName/PetNameNormalized/Notes -> null.
    /// </summary>
    private void ApplyReadModelChange(
        PaymentProjectionSnapshot snapshot,
        Guid eventId,
        DateTime occurredAtUtc,
        DateTime projectedAtUtc)
    {
        var existing = _queryDb.PaymentReadModels.Find(snapshot.PaymentId);

        // Defensive ordering guard; finance gate ile lockstep olduğundan normalde tetiklenmez.
        if (existing is not null && occurredAtUtc < existing.LastEventOccurredAtUtc)
            return;

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

        if (existing is null)
        {
            _queryDb.PaymentReadModels.Add(new PaymentReadModel
            {
                PaymentId = snapshot.PaymentId,
                TenantId = snapshot.TenantId,
                ClinicId = snapshot.ClinicId,
                ClientId = snapshot.ClientId,
                ClientName = clientName,
                ClientNameNormalized = clientNameNormalized,
                PetId = snapshot.PetId,
                PetName = petName,
                PetNameNormalized = petNameNormalized,
                Amount = snapshot.Amount,
                Currency = snapshot.Currency,
                Method = snapshot.Method,
                PaidAtUtc = snapshot.PaidAtUtc,
                Notes = notes,
                NotesNormalized = notesNormalized,
                AppointmentId = snapshot.AppointmentId,
                ExaminationId = snapshot.ExaminationId,
                LastEventId = eventId,
                LastEventOccurredAtUtc = occurredAtUtc,
                LastProjectedAtUtc = projectedAtUtc
            });
            return;
        }

        existing.TenantId = snapshot.TenantId;
        existing.ClinicId = snapshot.ClinicId;
        existing.ClientId = snapshot.ClientId;
        existing.ClientName = clientName;
        existing.ClientNameNormalized = clientNameNormalized;
        existing.PetId = snapshot.PetId;
        existing.PetName = petName;
        existing.PetNameNormalized = petNameNormalized;
        existing.Amount = snapshot.Amount;
        existing.Currency = snapshot.Currency;
        existing.Method = snapshot.Method;
        existing.PaidAtUtc = snapshot.PaidAtUtc;
        existing.Notes = notes;
        existing.NotesNormalized = notesNormalized;
        existing.AppointmentId = snapshot.AppointmentId;
        existing.ExaminationId = snapshot.ExaminationId;
        existing.LastEventId = eventId;
        existing.LastEventOccurredAtUtc = occurredAtUtc;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

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

    private static (PaymentProjectionSnapshot Snapshot, Guid EventId, DateTime OccurredAtUtc) DeserializeIntegrationEvent(
        string type, string payload)
    {
        var payloadType = PaymentIntegrationEventTypeRegistry.ResolvePayloadType(type);
        var deserialized = JsonSerializer.Deserialize(payload, payloadType, JsonOptions)
            ?? throw new InvalidOperationException($"Payment integration event deserialize edilemedi. Type={type}");

        return deserialized switch
        {
            PaymentCreatedIntegrationEvent created =>
                (created.Current, created.EventId, created.OccurredAtUtc),
            PaymentUpdatedIntegrationEvent updated =>
                (updated.Current, updated.EventId, updated.OccurredAtUtc),
            _ => throw new UnknownPaymentIntegrationEventTypeException(type)
        };
    }

    private readonly record struct DailyBucketKey(Guid TenantId, Guid ClinicId, DateOnly LocalDate, string Currency);
}
