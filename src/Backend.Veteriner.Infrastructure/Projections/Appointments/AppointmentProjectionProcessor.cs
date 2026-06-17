using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionProcessor : IAppointmentProjectionProcessor
{
    private static readonly SemaphoreSlim BatchGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly AppointmentProjectionOptions _options;
    private readonly OutboxOptions _outboxOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentProjectionProcessor> _logger;

    public AppointmentProjectionProcessor(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        IOptions<AppointmentProjectionOptions> options,
        IOptions<OutboxOptions> outboxOptions,
        TimeProvider timeProvider,
        ILogger<AppointmentProjectionProcessor> logger)
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
            return await ProcessBatchCoreAsync(cancellationToken);
        }
        finally
        {
            BatchGate.Release();
        }
    }

    private async Task<int> ProcessBatchCoreAsync(CancellationToken cancellationToken)
    {
        var batchStarted = _timeProvider.GetUtcNow();
        var now = batchStarted.UtcDateTime;
        var batchSize = Math.Max(1, _options.BatchSize);
        var processedCount = 0;
        var failedCount = 0;
        var deadLetteredCount = 0;
        DateTime? oldestPendingCreatedAtUtc = null;

        for (var i = 0; i < batchSize; i++)
        {
            var head = await GetStrictOrderHeadAsync(cancellationToken);
            if (head is null)
                break;

            oldestPendingCreatedAtUtc ??= head.CreatedAtUtc;

            if (head.NextAttemptAtUtc is { } nextAttempt && nextAttempt > now)
                break;

            try
            {
                await ProcessMessageAsync(head, cancellationToken);
                OutboxRetryHelper.ApplySuccess(head);
                await _commandDb.SaveChangesAsync(cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                OutboxRetryHelper.ApplyFailure(head, _outboxOptions, ex);

                if (head.DeadLetterAtUtc is not null)
                {
                    deadLetteredCount++;
                    _logger.LogError(ex,
                        "Appointment projection dead-letter. Type={Type} Id={Id} Retry={Retry}",
                        head.Type, head.Id, head.RetryCount);
                }
                else
                {
                    var backoff = OutboxRetryHelper.ComputeBackoff(_outboxOptions.BaseDelaySeconds, head.RetryCount);
                    _logger.LogWarning(ex,
                        "Appointment projection retry in {DelaySeconds}s. Type={Type} Id={Id} Retry={Retry}",
                        (int)backoff.TotalSeconds, head.Type, head.Id, head.RetryCount);
                }

                await _commandDb.SaveChangesAsync(cancellationToken);
                break;
            }
        }

        if (processedCount > 0 || failedCount > 0)
        {
            var durationMs = (_timeProvider.GetUtcNow() - batchStarted).TotalMilliseconds;
            var oldestPendingAgeMs = oldestPendingCreatedAtUtc is { } oldest
                ? (now - oldest).TotalMilliseconds
                : 0d;

            _logger.LogInformation(
                "Appointment projection batch completed. BatchRequested={BatchRequested} ProcessedCount={ProcessedCount} FailedCount={FailedCount} DeadLetteredCount={DeadLetteredCount} DurationMs={DurationMs} OldestPendingAgeMs={OldestPendingAgeMs} ConsumerName={ConsumerName}",
                batchSize,
                processedCount,
                failedCount,
                deadLetteredCount,
                (long)durationMs,
                (long)Math.Max(0, oldestPendingAgeMs),
                _options.ConsumerName);
        }

        return processedCount;
    }

    /// <summary>
    /// Global outbox sirasinda en eski islenmemis, dead-letter olmayan appointment mesajini dondurur.
    /// </summary>
    private Task<OutboxMessage?> GetStrictOrderHeadAsync(CancellationToken cancellationToken)
        => OutboxMessageQueryFilters
            .AppointmentIntegrationEventsOnly(_commandDb.OutboxMessages)
            .Where(m => m.ProcessedAtUtc == null && m.DeadLetterAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task ProcessMessageAsync(OutboxMessage msg, CancellationToken cancellationToken)
    {
        var payloadType = AppointmentIntegrationEventTypeRegistry.ResolvePayloadType(msg.Type);
        var integrationEvent = JsonSerializer.Deserialize(msg.Payload, payloadType, JsonOptions)
            ?? throw new InvalidOperationException($"Appointment integration event deserialize edilemedi. Type={msg.Type}");

        var eventId = ExtractEventId(integrationEvent);
        var projectedAtUtc = DateTime.UtcNow;

        var alreadyProcessed = await _queryDb.ProcessedProjectionEvents
            .AsNoTracking()
            .AnyAsync(
                x => x.EventId == eventId && x.ConsumerName == _options.ConsumerName,
                cancellationToken);

        if (alreadyProcessed)
        {
            return;
        }

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            alreadyProcessed = await _queryDb.ProcessedProjectionEvents
                .AnyAsync(
                    x => x.EventId == eventId && x.ConsumerName == _options.ConsumerName,
                    cancellationToken);

            if (!alreadyProcessed)
            {
                await ApplyEventAsync(integrationEvent, eventId, projectedAtUtc, cancellationToken);

                _queryDb.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
                {
                    EventId = eventId,
                    ConsumerName = _options.ConsumerName,
                    ProcessedAtUtc = projectedAtUtc
                });

                await _queryDb.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task ApplyEventAsync(
        object integrationEvent,
        Guid eventId,
        DateTime projectedAtUtc,
        CancellationToken cancellationToken)
    {
        switch (integrationEvent)
        {
            case AppointmentCreatedIntegrationEvent created:
                await ApplySnapshotChangeAsync(null, created.Current, eventId, projectedAtUtc, cancellationToken);
                break;

            case AppointmentUpdatedIntegrationEvent updated:
                await ApplySnapshotChangeAsync(updated.Previous, updated.Current, eventId, projectedAtUtc, cancellationToken);
                break;

            case AppointmentRescheduledIntegrationEvent rescheduled:
                await ApplySnapshotChangeAsync(rescheduled.Previous, rescheduled.Current, eventId, projectedAtUtc, cancellationToken);
                break;

            case AppointmentCancelledIntegrationEvent cancelled:
                await ApplySnapshotChangeAsync(cancelled.Previous, cancelled.Current, eventId, projectedAtUtc, cancellationToken);
                break;

            case AppointmentCompletedIntegrationEvent completed:
                await ApplySnapshotChangeAsync(completed.Previous, completed.Current, eventId, projectedAtUtc, cancellationToken);
                break;

            default:
                throw new InvalidOperationException(
                    $"Desteklenmeyen appointment integration event payload: {integrationEvent.GetType().Name}");
        }
    }

    private async Task ApplySnapshotChangeAsync(
        AppointmentProjectionSnapshot? previous,
        AppointmentProjectionSnapshot current,
        Guid eventId,
        DateTime projectedAtUtc,
        CancellationToken cancellationToken)
    {
        UpsertAppointmentReadModel(current, eventId, projectedAtUtc);
        await _queryDb.SaveChangesAsync(cancellationToken);

        foreach (var bucket in GetAffectedDailyBuckets(previous, current))
            RecalculateDailyStats(bucket.TenantId, bucket.ClinicId, bucket.LocalDate, eventId, projectedAtUtc);

        foreach (var key in GetAffectedPetKeys(previous, current))
            RecalculatePetActivity(key.TenantId, key.ClinicId, key.PetId, eventId, projectedAtUtc);

        foreach (var key in GetAffectedClientKeys(previous, current))
            RecalculateClientActivity(key.TenantId, key.ClinicId, key.ClientId, eventId, projectedAtUtc);
    }

    private void UpsertAppointmentReadModel(
        AppointmentProjectionSnapshot snap,
        Guid eventId,
        DateTime projectedAtUtc)
    {
        var scheduledEndUtc = snap.ScheduledAtUtc.AddMinutes(snap.DurationMinutes);
        var existing = _queryDb.AppointmentReadModels.Find(snap.AppointmentId);

        if (existing is null)
        {
            _queryDb.AppointmentReadModels.Add(MapToReadModel(snap, scheduledEndUtc, eventId, projectedAtUtc));
            return;
        }

        UpdateReadModel(existing, snap, scheduledEndUtc, eventId, projectedAtUtc);
    }

    private static AppointmentReadModel MapToReadModel(
        AppointmentProjectionSnapshot snap,
        DateTime scheduledEndUtc,
        Guid eventId,
        DateTime projectedAtUtc)
        => new()
        {
            AppointmentId = snap.AppointmentId,
            TenantId = snap.TenantId,
            ClinicId = snap.ClinicId,
            ClinicName = snap.ClinicName,
            PetId = snap.PetId,
            PetName = snap.PetName,
            SpeciesId = snap.SpeciesId,
            SpeciesName = snap.SpeciesName,
            ClientId = snap.ClientId,
            ClientName = snap.ClientName,
            ClientPhone = snap.ClientPhone,
            ClientPhoneNormalized = snap.ClientPhoneNormalized,
            ClientEmail = snap.ClientEmail,
            PetBreed = snap.PetBreed,
            PetBreedRefName = snap.PetBreedRefName,
            ScheduledAtUtc = snap.ScheduledAtUtc,
            ScheduledEndUtc = scheduledEndUtc,
            DurationMinutes = snap.DurationMinutes,
            AppointmentType = snap.AppointmentType,
            Status = snap.Status,
            Notes = snap.Notes,
            LastEventId = eventId,
            LastProjectedAtUtc = projectedAtUtc
        };

    private static void UpdateReadModel(
        AppointmentReadModel existing,
        AppointmentProjectionSnapshot snap,
        DateTime scheduledEndUtc,
        Guid eventId,
        DateTime projectedAtUtc)
    {
        existing.TenantId = snap.TenantId;
        existing.ClinicId = snap.ClinicId;
        existing.ClinicName = snap.ClinicName;
        existing.PetId = snap.PetId;
        existing.PetName = snap.PetName;
        existing.SpeciesId = snap.SpeciesId;
        existing.SpeciesName = snap.SpeciesName;
        existing.ClientId = snap.ClientId;
        existing.ClientName = snap.ClientName;
        existing.ClientPhone = snap.ClientPhone;
        existing.ClientPhoneNormalized = snap.ClientPhoneNormalized;
        existing.ClientEmail = snap.ClientEmail;
        existing.PetBreed = snap.PetBreed;
        existing.PetBreedRefName = snap.PetBreedRefName;
        existing.ScheduledAtUtc = snap.ScheduledAtUtc;
        existing.ScheduledEndUtc = scheduledEndUtc;
        existing.DurationMinutes = snap.DurationMinutes;
        existing.AppointmentType = snap.AppointmentType;
        existing.Status = snap.Status;
        existing.Notes = snap.Notes;
        existing.LastEventId = eventId;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    private void RecalculateDailyStats(
        Guid tenantId,
        Guid clinicId,
        DateOnly localDate,
        Guid eventId,
        DateTime projectedAtUtc)
    {
        var (dayStartUtc, dayEndUtc) = OperationDayBounds.ForLocalDate(localDate);

        var appointments = _queryDb.AppointmentReadModels
            .AsNoTracking()
            .Where(a =>
                a.TenantId == tenantId
                && a.ClinicId == clinicId
                && a.ScheduledAtUtc >= dayStartUtc
                && a.ScheduledAtUtc < dayEndUtc)
            .ToList();

        var scheduledCount = appointments.Count(a => a.Status == (int)AppointmentStatus.Scheduled);
        var completedCount = appointments.Count(a => a.Status == (int)AppointmentStatus.Completed);
        var cancelledCount = appointments.Count(a => a.Status == (int)AppointmentStatus.Cancelled);
        var totalCount = appointments.Count;

        var existing = _queryDb.ClinicDailyAppointmentStatsReadModels
            .Find(tenantId, clinicId, localDate);

        if (totalCount == 0)
        {
            if (existing is not null)
                _queryDb.ClinicDailyAppointmentStatsReadModels.Remove(existing);
            return;
        }

        if (existing is null)
        {
            _queryDb.ClinicDailyAppointmentStatsReadModels.Add(new ClinicDailyAppointmentStatsReadModel
            {
                TenantId = tenantId,
                ClinicId = clinicId,
                LocalDate = localDate,
                ScheduledCount = scheduledCount,
                CompletedCount = completedCount,
                CancelledCount = cancelledCount,
                TotalCount = totalCount,
                LastEventId = eventId,
                LastProjectedAtUtc = projectedAtUtc
            });
            return;
        }

        existing.ScheduledCount = scheduledCount;
        existing.CompletedCount = completedCount;
        existing.CancelledCount = cancelledCount;
        existing.TotalCount = totalCount;
        existing.LastEventId = eventId;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    private void RecalculatePetActivity(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid eventId,
        DateTime projectedAtUtc)
    {
        var latest = _queryDb.AppointmentReadModels
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ClinicId == clinicId && a.PetId == petId)
            .OrderByDescending(a => a.ScheduledAtUtc)
            .ThenBy(a => a.AppointmentId)
            .FirstOrDefault();

        var existing = _queryDb.ClinicPetActivityReadModels.Find(tenantId, clinicId, petId);

        if (latest is null)
        {
            if (existing is not null)
                _queryDb.ClinicPetActivityReadModels.Remove(existing);
            return;
        }

        if (existing is null)
        {
            _queryDb.ClinicPetActivityReadModels.Add(new ClinicPetActivityReadModel
            {
                TenantId = tenantId,
                ClinicId = clinicId,
                PetId = petId,
                ClientId = latest.ClientId,
                PetName = latest.PetName,
                SpeciesId = latest.SpeciesId,
                SpeciesName = latest.SpeciesName,
                LastAppointmentAtUtc = latest.ScheduledAtUtc,
                LastEventId = eventId,
                LastProjectedAtUtc = projectedAtUtc
            });
            return;
        }

        existing.ClientId = latest.ClientId;
        existing.PetName = latest.PetName;
        existing.SpeciesId = latest.SpeciesId;
        existing.SpeciesName = latest.SpeciesName;
        existing.LastAppointmentAtUtc = latest.ScheduledAtUtc;
        existing.LastEventId = eventId;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    private void RecalculateClientActivity(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid eventId,
        DateTime projectedAtUtc)
    {
        var latest = _queryDb.AppointmentReadModels
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ClinicId == clinicId && a.ClientId == clientId)
            .OrderByDescending(a => a.ScheduledAtUtc)
            .ThenBy(a => a.AppointmentId)
            .FirstOrDefault();

        var existing = _queryDb.ClinicClientActivityReadModels.Find(tenantId, clinicId, clientId);

        if (latest is null)
        {
            if (existing is not null)
                _queryDb.ClinicClientActivityReadModels.Remove(existing);
            return;
        }

        if (existing is null)
        {
            _queryDb.ClinicClientActivityReadModels.Add(new ClinicClientActivityReadModel
            {
                TenantId = tenantId,
                ClinicId = clinicId,
                ClientId = clientId,
                ClientName = latest.ClientName,
                ClientPhone = latest.ClientPhone,
                LastAppointmentAtUtc = latest.ScheduledAtUtc,
                LastEventId = eventId,
                LastProjectedAtUtc = projectedAtUtc
            });
            return;
        }

        existing.ClientName = latest.ClientName;
        existing.ClientPhone = latest.ClientPhone;
        existing.LastAppointmentAtUtc = latest.ScheduledAtUtc;
        existing.LastEventId = eventId;
        existing.LastProjectedAtUtc = projectedAtUtc;
    }

    private static HashSet<(Guid TenantId, Guid ClinicId, DateOnly LocalDate)> GetAffectedDailyBuckets(
        AppointmentProjectionSnapshot? previous,
        AppointmentProjectionSnapshot current)
    {
        var buckets = new HashSet<(Guid, Guid, DateOnly)>
        {
            (current.TenantId, current.ClinicId, OperationDayBounds.ToLocalDate(current.ScheduledAtUtc))
        };

        if (previous is not null)
            buckets.Add((previous.TenantId, previous.ClinicId, OperationDayBounds.ToLocalDate(previous.ScheduledAtUtc)));

        return buckets;
    }

    private static HashSet<(Guid TenantId, Guid ClinicId, Guid PetId)> GetAffectedPetKeys(
        AppointmentProjectionSnapshot? previous,
        AppointmentProjectionSnapshot current)
    {
        var keys = new HashSet<(Guid, Guid, Guid)>
        {
            (current.TenantId, current.ClinicId, current.PetId)
        };

        if (previous is not null)
            keys.Add((previous.TenantId, previous.ClinicId, previous.PetId));

        return keys;
    }

    private static HashSet<(Guid TenantId, Guid ClinicId, Guid ClientId)> GetAffectedClientKeys(
        AppointmentProjectionSnapshot? previous,
        AppointmentProjectionSnapshot current)
    {
        var keys = new HashSet<(Guid, Guid, Guid)>
        {
            (current.TenantId, current.ClinicId, current.ClientId)
        };

        if (previous is not null)
            keys.Add((previous.TenantId, previous.ClinicId, previous.ClientId));

        return keys;
    }

    private static Guid ExtractEventId(object integrationEvent)
        => integrationEvent switch
        {
            AppointmentCreatedIntegrationEvent e => e.EventId,
            AppointmentUpdatedIntegrationEvent e => e.EventId,
            AppointmentRescheduledIntegrationEvent e => e.EventId,
            AppointmentCancelledIntegrationEvent e => e.EventId,
            AppointmentCompletedIntegrationEvent e => e.EventId,
            _ => throw new InvalidOperationException("Appointment integration event EventId çözülemedi.")
        };
}
