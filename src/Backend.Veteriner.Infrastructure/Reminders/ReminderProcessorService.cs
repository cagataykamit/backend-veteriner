using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Reminders;

public sealed class ReminderProcessorService
{
    private readonly AppDbContext _db;
    private readonly IReminderEmailOutboxEnqueuer _reminderEmailOutboxEnqueuer;
    private readonly ReminderProcessorOptions _options;
    private readonly ILogger<ReminderProcessorService> _logger;

    public ReminderProcessorService(
        AppDbContext db,
        IReminderEmailOutboxEnqueuer reminderEmailOutboxEnqueuer,
        IOptions<ReminderProcessorOptions> options,
        ILogger<ReminderProcessorService> logger)
    {
        _db = db;
        _reminderEmailOutboxEnqueuer = reminderEmailOutboxEnqueuer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessOnceAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
            return;

        var batchSize = Math.Clamp(_options.BatchSize, 1, 500);
        var toleranceMinutes = Math.Clamp(_options.WindowToleranceMinutes, 1, 120);
        var legacyReconcileAfterMinutes = Math.Clamp(_options.LegacyEnqueuedReconcileAfterMinutes, 5, 24 * 60);
        var nowUtc = DateTime.UtcNow;
        var toleranceStartUtc = nowUtc.AddMinutes(-toleranceMinutes);

        var settingsRows = await _db.TenantReminderSettings
            .AsNoTracking()
            .Where(x => x.EmailChannelEnabled && (x.AppointmentRemindersEnabled || x.VaccinationRemindersEnabled))
            .ToListAsync(ct);

        var tenantIds = settingsRows.Select(x => x.TenantId).Distinct().ToArray();
        var activeTenantIds = await _db.Tenants
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id) && t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var subscriptions = await _db.TenantSubscriptions
            .AsNoTracking()
            .Where(s => activeTenantIds.Contains(s.TenantId))
            .ToListAsync(ct);
        var subscriptionsByTenant = subscriptions.ToDictionary(x => x.TenantId);

        foreach (var settings in settingsRows)
        {
            if (!activeTenantIds.Contains(settings.TenantId))
                continue;

            if (!subscriptionsByTenant.TryGetValue(settings.TenantId, out var sub))
                continue;

            var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, nowUtc);
            if (!TenantSubscriptionEffectiveWriteEvaluator.WriteAllowed(effective))
                continue;

            if (settings.AppointmentRemindersEnabled)
            {
                await ProcessAppointmentRemindersAsync(
                    settings.TenantId,
                    settings.AppointmentReminderHoursBefore,
                    nowUtc,
                    toleranceStartUtc,
                    batchSize,
                    ct);
            }

            if (settings.VaccinationRemindersEnabled)
            {
                await ProcessVaccinationRemindersAsync(
                    settings.TenantId,
                    settings.VaccinationReminderDaysBefore,
                    nowUtc,
                    toleranceStartUtc,
                    batchSize,
                    ct);
            }
        }

        await SyncEnqueuedDispatchLogsWithOutboxStatusAsync(batchSize, ct);
        await ReconcileLegacyEnqueuedLogsWithoutOutboxIdAsync(batchSize, nowUtc, legacyReconcileAfterMinutes, ct);
    }

    private async Task ProcessAppointmentRemindersAsync(
        Guid tenantId,
        int hoursBefore,
        DateTime nowUtc,
        DateTime toleranceStartUtc,
        int batchSize,
        CancellationToken ct)
    {
        var minScheduledUtc = toleranceStartUtc.AddHours(hoursBefore);
        var maxScheduledUtc = nowUtc.AddHours(hoursBefore);

        var candidates = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && a.Status == AppointmentStatus.Scheduled
                && a.ScheduledAtUtc > nowUtc
                && a.ScheduledAtUtc >= minScheduledUtc
                && a.ScheduledAtUtc <= maxScheduledUtc)
            .OrderBy(a => a.ScheduledAtUtc)
            .Select(a => new AppointmentReminderCandidateRow(a.Id, a.ClinicId, a.PetId, a.ScheduledAtUtc, a.AppointmentType))
            .Take(batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        var petIds = candidates.Select(x => x.PetId).Distinct().ToArray();
        var petRows = await _db.Pets
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && petIds.Contains(p.Id))
            .Select(p => new PetClientRow(p.Id, p.ClientId, p.Name))
            .ToListAsync(ct);
        var petById = petRows.ToDictionary(x => x.PetId);

        var clientIds = petRows.Select(x => x.ClientId).Distinct().ToArray();
        var clientRows = await _db.Clients
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && clientIds.Contains(c.Id))
            .Select(c => new ClientRecipientRow(c.Id, c.FullName, c.Email))
            .ToListAsync(ct);
        var clientById = clientRows.ToDictionary(x => x.ClientId);

        var clinicIds = candidates.Select(x => x.ClinicId).Distinct().ToArray();
        var clinicRows = await _db.Clinics
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && clinicIds.Contains(c.Id))
            .Select(c => new ClinicNameRow(c.Id, c.Name))
            .ToListAsync(ct);
        var clinicById = clinicRows.ToDictionary(x => x.ClinicId);

        var dedupeKeys = candidates
            .Select(c => BuildAppointmentDedupeKey(c.AppointmentId, hoursBefore))
            .Distinct()
            .ToArray();
        var existingDedupe = await _db.ReminderDispatchLogs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && dedupeKeys.Contains(x.DedupeKey))
            .Select(x => x.DedupeKey)
            .ToListAsync(ct);
        var dedupeSet = existingDedupe.ToHashSet(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var dedupeKey = BuildAppointmentDedupeKey(candidate.AppointmentId, hoursBefore);
            if (dedupeSet.Contains(dedupeKey))
                continue;

            var reminderDueAtUtc = candidate.ScheduledAtUtc.AddHours(-hoursBefore);
            var pet = petById.TryGetValue(candidate.PetId, out var petRow) ? petRow : null;
            var client = pet is null
                ? null
                : clientById.TryGetValue(pet.ClientId, out var clientRow) ? clientRow : null;
            var clinicName = clinicById.TryGetValue(candidate.ClinicId, out var clinicRow)
                ? clinicRow.Name
                : "Klinik";

            var recipientEmail = client?.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var recipientName = string.IsNullOrWhiteSpace(client?.FullName) ? "Müşterimiz" : client!.FullName.Trim();
            var petName = string.IsNullOrWhiteSpace(pet?.Name) ? "Dostunuz" : pet!.Name.Trim();

            var log = new ReminderDispatchLog(
                tenantId,
                candidate.ClinicId,
                ReminderType.Appointment,
                ReminderSourceEntityType.Appointment,
                candidate.AppointmentId,
                recipientEmail,
                recipientName,
                candidate.ScheduledAtUtc,
                reminderDueAtUtc,
                ReminderDispatchStatus.Pending,
                dedupeKey);

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                log.MarkSkipped("Recipient email is missing");
                if (await TryInsertLogAsync(log, ct))
                    dedupeSet.Add(dedupeKey);
                continue;
            }

            try
            {
                var subject = "Randevu hatırlatması";
                var body =
                    $"Sayın {recipientName},\n" +
                    $"{petName} için {clinicName} kliniğinde {FormatForDisplay(candidate.ScheduledAtUtc)} tarihinde planlanan randevunuzu hatırlatırız.";

                var outboxMessageId = await _reminderEmailOutboxEnqueuer.EnqueueReminderEmailAsync(
                    recipientEmail,
                    subject,
                    body,
                    isHtml: false,
                    ct);
                log.MarkEnqueued(outboxMessageId);
            }
            catch (Exception ex)
            {
                log.MarkFailed(ex.Message);
                _logger.LogWarning(ex, "Appointment reminder enqueue failed. Tenant={TenantId} Appointment={AppointmentId}", tenantId, candidate.AppointmentId);
            }

            if (await TryInsertLogAsync(log, ct, detachOutboxMessageIdOnConflict: log.OutboxMessageId))
                dedupeSet.Add(dedupeKey);
        }
    }

    private async Task ProcessVaccinationRemindersAsync(
        Guid tenantId,
        int daysBefore,
        DateTime nowUtc,
        DateTime toleranceStartUtc,
        int batchSize,
        CancellationToken ct)
    {
        var minDueUtc = toleranceStartUtc.AddDays(daysBefore);
        var maxDueUtc = nowUtc.AddDays(daysBefore);

        var candidates = await _db.Vaccinations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && v.Status == VaccinationStatus.Scheduled
                && v.DueAtUtc.HasValue
                && v.DueAtUtc.Value > nowUtc
                && v.DueAtUtc.Value >= minDueUtc
                && v.DueAtUtc.Value <= maxDueUtc)
            .OrderBy(v => v.DueAtUtc)
            .Select(v => new VaccinationReminderCandidateRow(v.Id, v.ClinicId, v.PetId, v.DueAtUtc!.Value, v.VaccineName))
            .Take(batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        var petIds = candidates.Select(x => x.PetId).Distinct().ToArray();
        var petRows = await _db.Pets
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && petIds.Contains(p.Id))
            .Select(p => new PetClientRow(p.Id, p.ClientId, p.Name))
            .ToListAsync(ct);
        var petById = petRows.ToDictionary(x => x.PetId);

        var clientIds = petRows.Select(x => x.ClientId).Distinct().ToArray();
        var clientRows = await _db.Clients
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && clientIds.Contains(c.Id))
            .Select(c => new ClientRecipientRow(c.Id, c.FullName, c.Email))
            .ToListAsync(ct);
        var clientById = clientRows.ToDictionary(x => x.ClientId);

        var clinicIds = candidates.Select(x => x.ClinicId).Distinct().ToArray();
        var clinicRows = await _db.Clinics
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && clinicIds.Contains(c.Id))
            .Select(c => new ClinicNameRow(c.Id, c.Name))
            .ToListAsync(ct);
        var clinicById = clinicRows.ToDictionary(x => x.ClinicId);

        var dedupeKeys = candidates
            .Select(c => BuildVaccinationDedupeKey(c.VaccinationId, daysBefore))
            .Distinct()
            .ToArray();
        var existingDedupe = await _db.ReminderDispatchLogs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && dedupeKeys.Contains(x.DedupeKey))
            .Select(x => x.DedupeKey)
            .ToListAsync(ct);
        var dedupeSet = existingDedupe.ToHashSet(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var dedupeKey = BuildVaccinationDedupeKey(candidate.VaccinationId, daysBefore);
            if (dedupeSet.Contains(dedupeKey))
                continue;

            var reminderDueAtUtc = candidate.DueAtUtc.AddDays(-daysBefore);
            var pet = petById.TryGetValue(candidate.PetId, out var petRow) ? petRow : null;
            var client = pet is null
                ? null
                : clientById.TryGetValue(pet.ClientId, out var clientRow) ? clientRow : null;
            var clinicName = clinicById.TryGetValue(candidate.ClinicId, out var clinicRow)
                ? clinicRow.Name
                : "Klinik";

            var recipientEmail = client?.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var recipientName = string.IsNullOrWhiteSpace(client?.FullName) ? "Müşterimiz" : client!.FullName.Trim();
            var petName = string.IsNullOrWhiteSpace(pet?.Name) ? "Dostunuz" : pet!.Name.Trim();

            var log = new ReminderDispatchLog(
                tenantId,
                candidate.ClinicId,
                ReminderType.Vaccination,
                ReminderSourceEntityType.Vaccination,
                candidate.VaccinationId,
                recipientEmail,
                recipientName,
                candidate.DueAtUtc,
                reminderDueAtUtc,
                ReminderDispatchStatus.Pending,
                dedupeKey);

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                log.MarkSkipped("Recipient email is missing");
                if (await TryInsertLogAsync(log, ct))
                    dedupeSet.Add(dedupeKey);
                continue;
            }

            try
            {
                var subject = "Aşı hatırlatması";
                var body =
                    $"Sayın {recipientName},\n" +
                    $"{petName} için {candidate.VaccineName} aşısının {FormatForDisplay(candidate.DueAtUtc)} tarihinde planlandığını hatırlatırız. ({clinicName})";

                var outboxMessageId = await _reminderEmailOutboxEnqueuer.EnqueueReminderEmailAsync(
                    recipientEmail,
                    subject,
                    body,
                    isHtml: false,
                    ct);
                log.MarkEnqueued(outboxMessageId);
            }
            catch (Exception ex)
            {
                log.MarkFailed(ex.Message);
                _logger.LogWarning(ex, "Vaccination reminder enqueue failed. Tenant={TenantId} Vaccination={VaccinationId}", tenantId, candidate.VaccinationId);
            }

            if (await TryInsertLogAsync(log, ct, detachOutboxMessageIdOnConflict: log.OutboxMessageId))
                dedupeSet.Add(dedupeKey);
        }
    }

    private async Task SyncEnqueuedDispatchLogsWithOutboxStatusAsync(int batchSize, CancellationToken ct)
    {
        var enqueuedLogs = await _db.ReminderDispatchLogs
            .Where(x => x.Status == ReminderDispatchStatus.Enqueued && x.OutboxMessageId != null)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

        if (enqueuedLogs.Count == 0)
            return;

        var outboxIds = enqueuedLogs
            .Select(x => x.OutboxMessageId!.Value)
            .Distinct()
            .ToArray();

        var outboxById = await _db.OutboxMessages
            .AsNoTracking()
            .Where(x => outboxIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var hasChanges = false;
        foreach (var log in enqueuedLogs)
        {
            if (log.OutboxMessageId is null)
                continue;

            if (!outboxById.TryGetValue(log.OutboxMessageId.Value, out var outbox))
                continue;

            if (outbox.ProcessedAtUtc.HasValue)
            {
                log.MarkSent(outbox.ProcessedAtUtc.Value);
                hasChanges = true;
                continue;
            }

            if (outbox.DeadLetterAtUtc.HasValue)
            {
                log.MarkFailed(
                    outbox.DeadLetterAtUtc.Value,
                    BuildOutboxFailureSummary(outbox));
                hasChanges = true;
            }
        }

        if (hasChanges)
            await _db.SaveChangesAsync(ct);
    }

    private async Task ReconcileLegacyEnqueuedLogsWithoutOutboxIdAsync(
        int batchSize,
        DateTime nowUtc,
        int staleAfterMinutes,
        CancellationToken ct)
    {
        var cutoffUtc = nowUtc.AddMinutes(-staleAfterMinutes);
        var legacyLogs = await _db.ReminderDispatchLogs
            .Where(x => x.Status == ReminderDispatchStatus.Enqueued
                        && x.OutboxMessageId == null
                        && x.CreatedAtUtc <= cutoffUtc)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

        if (legacyLogs.Count == 0)
            return;

        const string message = "Gönderim sonucu doğrulanamadı. Lütfen gerekirse yeniden hatırlatma oluşturun.";
        foreach (var log in legacyLogs)
            log.MarkFailed(nowUtc, message);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<bool> TryInsertLogAsync(ReminderDispatchLog log, CancellationToken ct, Guid? detachOutboxMessageIdOnConflict = null)
    {
        try
        {
            await _db.ReminderDispatchLogs.AddAsync(log, ct);
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsDedupeConflict(ex))
        {
            _db.Entry(log).State = EntityState.Detached;
            if (detachOutboxMessageIdOnConflict.HasValue)
            {
                var outboxEntry = _db.ChangeTracker.Entries<OutboxMessage>()
                    .FirstOrDefault(x => x.Entity.Id == detachOutboxMessageIdOnConflict.Value && x.State == EntityState.Added);
                if (outboxEntry is not null)
                    outboxEntry.State = EntityState.Detached;
            }

            _logger.LogDebug("Reminder dedupe conflict skipped. Tenant={TenantId} DedupeKey={DedupeKey}", log.TenantId, log.DedupeKey);
            return false;
        }
    }

    private static string BuildOutboxFailureSummary(OutboxMessage outboxMessage)
    {
        if (!string.IsNullOrWhiteSpace(outboxMessage.LastError))
            return outboxMessage.LastError!;
        if (!string.IsNullOrWhiteSpace(outboxMessage.Error))
            return outboxMessage.Error!.Length > 1000 ? outboxMessage.Error[..1000] : outboxMessage.Error;
        return "Outbox message moved to dead-letter.";
    }

    private static bool IsDedupeConflict(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("IX_ReminderDispatchLogs_TenantId_DedupeKey", StringComparison.OrdinalIgnoreCase) == true
           || ex.Message.Contains("IX_ReminderDispatchLogs_TenantId_DedupeKey", StringComparison.OrdinalIgnoreCase);

    private static string BuildAppointmentDedupeKey(Guid appointmentId, int hoursBefore)
        => $"appointment:{appointmentId:D}:hours-before:{hoursBefore}";

    private static string BuildVaccinationDedupeKey(Guid vaccinationId, int daysBefore)
        => $"vaccination:{vaccinationId:D}:days-before:{daysBefore}";

    private static string FormatForDisplay(DateTime utcDateTime)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), tz);
            return local.ToString("dd.MM.yyyy HH:mm");
        }
        catch
        {
            return DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc).ToString("dd.MM.yyyy HH:mm 'UTC'");
        }
    }

    private sealed record AppointmentReminderCandidateRow(
        Guid AppointmentId,
        Guid ClinicId,
        Guid PetId,
        DateTime ScheduledAtUtc,
        AppointmentType AppointmentType);

    private sealed record VaccinationReminderCandidateRow(
        Guid VaccinationId,
        Guid ClinicId,
        Guid PetId,
        DateTime DueAtUtc,
        string VaccineName);

    private sealed record PetClientRow(Guid PetId, Guid ClientId, string Name);
    private sealed record ClientRecipientRow(Guid ClientId, string FullName, string? Email);
    private sealed record ClinicNameRow(Guid ClinicId, string Name);
}
