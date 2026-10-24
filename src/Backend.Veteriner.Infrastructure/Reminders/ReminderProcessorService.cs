using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
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
        var legacyReconcileAfterMinutes = Math.Clamp(_options.LegacyEnqueuedReconcileAfterMinutes, 5, 24 * 60);
        var nowUtc = DateTime.UtcNow;

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

            // Deneme kiracısında günlük e-posta tavanı (TRIAL-004); randevu ve aşı hatırlatmaları aynı bütçeyi paylaşır.
            var dailyBudget = await LoadDailyEmailBudgetAsync(settings.TenantId, sub, nowUtc, ct);

            if (settings.AppointmentRemindersEnabled)
            {
                await ProcessAppointmentRemindersAsync(
                    settings.TenantId,
                    settings.AppointmentReminderHoursBefore,
                    nowUtc,
                    batchSize,
                    dailyBudget,
                    ct);
            }

            if (settings.VaccinationRemindersEnabled)
            {
                await ProcessVaccinationRemindersAsync(
                    settings.TenantId,
                    settings.VaccinationReminderDaysBefore,
                    nowUtc,
                    batchSize,
                    dailyBudget,
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
        int batchSize,
        DailyEmailBudget dailyBudget,
        CancellationToken ct)
    {
        // Reminder due = ScheduledAtUtc - hoursBefore. Include "missed window": reminder time may be in the past
        // as long as the appointment is still in the future and within the lookahead (ScheduledAtUtc <= now + hoursBefore).
        var maxScheduledUtc = nowUtc.AddHours(hoursBefore);
        var dedupeSuffix = AppointmentDedupeSuffix(hoursBefore);
        var eligiblePetIds = ReminderEligiblePetIds(tenantId);

        // Günlüğü yazılmış aday tur sınırına (batchSize) sayılmaz: aksi halde pencerede batchSize'dan fazla aday varken
        // aynı ilk adaylar her turda yeniden seçilir ve kalanlar hiç değerlendirilmez.
        var candidates = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && a.Status == AppointmentStatus.Scheduled
                && a.ScheduledAtUtc > nowUtc
                && a.ScheduledAtUtc <= maxScheduledUtc
                && eligiblePetIds.Contains(a.PetId)
                && !_db.ReminderDispatchLogs.Any(l => l.TenantId == tenantId
                    && l.SourceEntityType == ReminderSourceEntityType.Appointment
                    && l.SourceEntityId == a.Id
                    && l.DedupeKey.EndsWith(dedupeSuffix)))
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

            if (!dailyBudget.TryConsume())
            {
                log.MarkSkipped(TrialLimits.ReminderDailyLimitSkipReason);
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
        int batchSize,
        DailyEmailBudget dailyBudget,
        CancellationToken ct)
    {
        // Reminder due = DueAtUtc - daysBefore. Include "missed window": reminder time may be in the past
        // as long as DueAtUtc is still in the future and within the lookahead (DueAtUtc <= now + daysBefore).
        var maxDueUtc = nowUtc.AddDays(daysBefore);
        var dedupeSuffix = VaccinationDedupeSuffix(daysBefore);
        var eligiblePetIds = ReminderEligiblePetIds(tenantId);

        // ADR-019 K3 (dışlamanın tek yeri): içe aktarılan aşı, partisinin hatırlatmaları yönetici tarafından serbest
        // bırakılana kadar aday değildir; dışlanan kayıt için dispatch log da yazılmaz. Günlüğü yazılmış aday tur
        // sınırına sayılmaz (randevu ile aynı gerekçe; içe aktarılan aşıların vadesi aynı ana düşer).
        var candidates = await _db.Vaccinations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && (v.Status == VaccinationStatus.Scheduled || v.Status == VaccinationStatus.Applied)
                && v.DueAtUtc.HasValue
                && v.DueAtUtc.Value > nowUtc
                && v.DueAtUtc.Value <= maxDueUtc
                && eligiblePetIds.Contains(v.PetId)
                && (v.ImportBatchId == null
                    || _db.ImportBatches.Any(b => b.Id == v.ImportBatchId && b.RemindersReleasedAtUtc != null))
                && !_db.ReminderDispatchLogs.Any(l => l.TenantId == tenantId
                    && l.SourceEntityType == ReminderSourceEntityType.Vaccination
                    && l.SourceEntityId == v.Id
                    && l.DedupeKey.EndsWith(dedupeSuffix)))
            .OrderBy(v => v.DueAtUtc)
            .Select(v => new VaccinationReminderCandidateRow(
                v.Id,
                v.ClinicId,
                v.PetId,
                v.DueAtUtc!.Value,
                v.VaccineName,
                v.Status))
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

            if (!dailyBudget.TryConsume())
            {
                log.MarkSkipped(TrialLimits.ReminderDailyLimitSkipReason);
                if (await TryInsertLogAsync(log, ct))
                    dedupeSet.Add(dedupeKey);
                continue;
            }

            try
            {
                var subject = "Aşı hatırlatması";
                var body = candidate.Status == VaccinationStatus.Applied
                    ? $"Sayın {recipientName},\n" +
                      $"{petName} için {candidate.VaccineName} aşısı için sonraki uygulama tarihinin {FormatForDisplay(candidate.DueAtUtc)} olduğunu hatırlatırız. ({clinicName})"
                    : $"Sayın {recipientName},\n" +
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

    /// <summary>
    /// ADR-020 S5 (dışlamanın tek yeri): vefat eden hayvan (<see cref="Pet.ReceivesCare"/>) ve hatırlatma istemeyen müşteri
    /// (<see cref="Client.AcceptsReminders"/>) aday değildir. Süzgeç aday sorgusunun içindedir: dışlanan kayıt tur sınırına
    /// (batchSize) sayılmaz ve dispatch log yazılmaz; durum geri alınırsa kayıt yeniden aday olur.
    /// </summary>
    private IQueryable<Guid> ReminderEligiblePetIds(Guid tenantId)
    {
        var acceptingClientIds = _db.Clients
            .Where(c => c.TenantId == tenantId)
            .Where(Client.AcceptsReminders)
            .Select(c => c.Id);

        return _db.Pets
            .Where(p => p.TenantId == tenantId && acceptingClientIds.Contains(p.ClientId))
            .Where(Pet.ReceivesCare)
            .Select(p => p.Id);
    }

    /// <summary>
    /// TRIAL-004: deneme kiracısında UTC gün içinde kuyruğa alınmış (Skipped dışı) günlük sayısı
    /// <see cref="TrialLimits.ReminderEmailsPerDay"/>'den düşülür; ücretli kiracıda bütçe sınırsızdır.
    /// </summary>
    private async Task<DailyEmailBudget> LoadDailyEmailBudgetAsync(
        Guid tenantId,
        TenantSubscription subscription,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!TrialLimitPolicy.IsTrial(subscription))
            return DailyEmailBudget.Unlimited;

        var dayStartUtc = nowUtc.Date;
        var dayEndUtc = dayStartUtc.AddDays(1);
        var sentToday = await _db.ReminderDispatchLogs
            .AsNoTracking()
            .CountAsync(
                l => l.TenantId == tenantId
                    && l.Status != ReminderDispatchStatus.Skipped
                    && l.CreatedAtUtc >= dayStartUtc
                    && l.CreatedAtUtc < dayEndUtc,
                ct);

        return new DailyEmailBudget(Math.Max(0, TrialLimits.ReminderEmailsPerDay - sentToday));
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
        => $"appointment:{appointmentId:D}{AppointmentDedupeSuffix(hoursBefore)}";

    private static string AppointmentDedupeSuffix(int hoursBefore) => $":hours-before:{hoursBefore}";

    private static string BuildVaccinationDedupeKey(Guid vaccinationId, int daysBefore)
        => $"vaccination:{vaccinationId:D}{VaccinationDedupeSuffix(daysBefore)}";

    private static string VaccinationDedupeSuffix(int daysBefore) => $":days-before:{daysBefore}";

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
        string VaccineName,
        VaccinationStatus Status);

    /// <summary>Kiracının bu turda kalan günlük e-posta hakkı; <c>null</c> kalan = sınırsız.</summary>
    private sealed class DailyEmailBudget
    {
        public static DailyEmailBudget Unlimited { get; } = new(null);

        private int? _remaining;

        public DailyEmailBudget(int? remaining) => _remaining = remaining;

        public bool TryConsume()
        {
            if (_remaining is null)
                return true;
            if (_remaining <= 0)
                return false;

            _remaining--;
            return true;
        }
    }

    private sealed record PetClientRow(Guid PetId, Guid ClientId, string Name);
    private sealed record ClientRecipientRow(Guid ClientId, string FullName, string? Email);
    private sealed record ClinicNameRow(Guid ClinicId, string Name);
}
