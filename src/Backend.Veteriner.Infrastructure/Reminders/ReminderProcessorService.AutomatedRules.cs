using Backend.Veteriner.Application.OwnerPortal.MarketingOptOut;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Reminders;

/// <summary>
/// Otomatik hatırlatıcı kuralları (TIER-003; docs/AUTOMATED_REMINDERS_API_CONTRACT.md). Aday dışlama mevcut kuraldır
/// (<see cref="ReminderEligiblePetIds"/>, <see cref="Client.AcceptsReminders"/>); dışlanan kayıt günlüğe yazılmaz, durum geri alınırsa yeniden
/// aday olur. Tekrar gönderim <see cref="ReminderDispatchLog.DedupeKey"/> benzersiz indeksiyle engellenir. Metin asgari veri taşır:
/// teşhis, tedavi adı ve tutar yazılmaz.
/// </summary>
public sealed partial class ReminderProcessorService
{
    private const string OptOutFooter = "\nBu iletileri almak istemiyorsanız kliniğinize bildirebilirsiniz.";

    private async Task ProcessAutomatedRuleAsync(
        ClinicReminderRule rule,
        DateTime nowUtc,
        int batchSize,
        DailyEmailBudget dailyBudget,
        CancellationToken ct)
    {
        // Ticari ileti yalnız kliniğin ticari ileti kapısı açıkken aranır (ADR-046); hatırlatmalar kapıdan bağımsızdır.
        if (ClinicReminderRuleDefinition.Of(rule.RuleType).IsMarketing
            && !await _db.ClinicMarketingSettings.AsNoTracking()
                .AnyAsync(s => s.TenantId == rule.TenantId && s.ClinicId == rule.ClinicId && s.IsEnabled, ct))
            return;

        if (!await _db.Clinics.AsNoTracking().AnyAsync(c => c.Id == rule.ClinicId && c.IsActive, ct))
            return;

        switch (rule.RuleType)
        {
            case ClinicReminderRuleType.FollowUp:
                await ProcessFollowUpRuleAsync(rule, nowUtc, batchSize, dailyBudget, ct);
                break;
            case ClinicReminderRuleType.Debt:
                await ProcessDebtRuleAsync(rule, nowUtc, batchSize, dailyBudget, ct);
                break;
            case ClinicReminderRuleType.NoReturn:
                await ProcessNoReturnRuleAsync(rule, nowUtc, batchSize, dailyBudget, ct);
                break;
        }
    }

    /// <summary>Kontrol tarihi önümüzdeki N gün içinde olan tedavi; hayvanın planlı randevusu varsa randevu hatırlatması yeter.</summary>
    private async Task ProcessFollowUpRuleAsync(
        ClinicReminderRule rule,
        DateTime nowUtc,
        int batchSize,
        DailyEmailBudget dailyBudget,
        CancellationToken ct)
    {
        var tenantId = rule.TenantId;
        var maxDueUtc = nowUtc.AddDays(rule.Days);
        var eligiblePetIds = ReminderEligiblePetIds(tenantId);

        var candidates = await _db.Treatments
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId
                && t.ClinicId == rule.ClinicId
                && t.FollowUpDateUtc != null
                && t.FollowUpDateUtc > nowUtc
                && t.FollowUpDateUtc <= maxDueUtc
                && eligiblePetIds.Contains(t.PetId)
                && !_db.Appointments.Any(a => a.TenantId == tenantId
                    && a.PetId == t.PetId
                    && a.Status == AppointmentStatus.Scheduled
                    && a.ScheduledAtUtc > nowUtc)
                && !_db.ReminderDispatchLogs.Any(l => l.TenantId == tenantId
                    && l.SourceEntityType == ReminderSourceEntityType.Treatment
                    && l.SourceEntityId == t.Id
                    && l.ScheduledForUtc == t.FollowUpDateUtc))
            .OrderBy(t => t.FollowUpDateUtc)
            .Select(t => new { t.Id, t.PetId, FollowUpUtc = t.FollowUpDateUtc!.Value })
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var recipient = await LoadPetRecipientAsync(tenantId, candidate.PetId, ct);
            var clinicName = await LoadClinicNameAsync(tenantId, rule.ClinicId, ct);
            var log = new ReminderDispatchLog(
                tenantId,
                rule.ClinicId,
                ReminderType.FollowUp,
                ReminderSourceEntityType.Treatment,
                candidate.Id,
                recipient.Email,
                recipient.Name,
                candidate.FollowUpUtc,
                candidate.FollowUpUtc.AddDays(-rule.Days),
                ReminderDispatchStatus.Pending,
                $"followup:{candidate.Id:D}:{candidate.FollowUpUtc:yyyyMMdd}",
                nowUtc);

            await EnqueueAutomatedEmailAsync(
                log,
                "Kontrol hatırlatması",
                $"Sayın {recipient.Name},\n{recipient.PetName} için {clinicName} kliniğindeki kontrol tarihiniz {FormatDateForDisplay(candidate.FollowUpUtc)}. Randevu için kliniğinizle görüşebilirsiniz.{OptOutFooter}",
                dailyBudget,
                ct);
        }
    }

    /// <summary>
    /// Ödenmemiş ücretin üzerinden N gün geçen müşteri; müşteri başına yalnız en eski ücret aday olur, hatırlatma o ücret ödenene kadar
    /// yinelenmez. Bağlanmamış ödemesi ya da açık kredisi olan müşteri (mutabakat kliniktedir) ve ücreti vefat eden hayvana ait kayıt dışlanır.
    /// </summary>
    private async Task ProcessDebtRuleAsync(
        ClinicReminderRule rule,
        DateTime nowUtc,
        int batchSize,
        DailyEmailBudget dailyBudget,
        CancellationToken ct)
    {
        if (rule.EnabledAtUtc is not { } enabledAtUtc)
            return;

        var tenantId = rule.TenantId;
        var clinicId = rule.ClinicId;
        var dueCutoffUtc = nowUtc.AddDays(-rule.Days);
        var floorUtc = enabledAtUtc.AddDays(-rule.Days);
        var eligiblePetIds = ReminderEligiblePetIds(tenantId);
        var acceptingClientIds = _db.Clients.Where(c => c.TenantId == tenantId).Where(Client.AcceptsReminders).Select(c => c.Id);

        var inWindow = _db.Charges.Where(c => c.TenantId == tenantId
            && c.ClinicId == clinicId
            && c.VoidedAtUtc == null
            && c.Amount > c.AllocatedAmount
            && c.CreatedAtUtc <= dueCutoffUtc
            && c.CreatedAtUtc >= floorUtc);

        var candidates = await inWindow
            .AsNoTracking()
            .Where(c => acceptingClientIds.Contains(c.ClientId)
                && (c.PetId == null || eligiblePetIds.Contains(c.PetId.Value))
                && !inWindow.Any(o => o.ClientId == c.ClientId
                    && (o.CreatedAtUtc < c.CreatedAtUtc || (o.CreatedAtUtc == c.CreatedAtUtc && o.Id.CompareTo(c.Id) < 0)))
                && !_db.Payments.Any(p => p.TenantId == tenantId
                    && p.ClinicId == clinicId
                    && p.ClientId == c.ClientId
                    && p.VoidedAtUtc == null
                    && p.Amount > p.AllocatedAmount + p.CreditedAmount)
                && !_db.ClientCredits.Any(k => k.TenantId == tenantId
                    && k.ClinicId == clinicId
                    && k.ClientId == c.ClientId
                    && k.VoidedAtUtc == null
                    && k.RefundedAtUtc == null
                    && k.Amount > k.UsedAmount)
                && !_db.ReminderDispatchLogs.Any(l => l.TenantId == tenantId
                    && l.SourceEntityType == ReminderSourceEntityType.Charge
                    && l.SourceEntityId == c.Id))
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new { c.Id, c.ClientId, c.CreatedAtUtc })
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var client = await _db.Clients
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == candidate.ClientId)
                .Select(c => new { c.FullName, c.Email })
                .FirstOrDefaultAsync(ct);
            var recipientName = string.IsNullOrWhiteSpace(client?.FullName) ? "Müşterimiz" : client!.FullName.Trim();
            var clinicName = await LoadClinicNameAsync(tenantId, clinicId, ct);
            var log = new ReminderDispatchLog(
                tenantId,
                clinicId,
                ReminderType.Debt,
                ReminderSourceEntityType.Charge,
                candidate.Id,
                client?.Email?.Trim().ToLowerInvariant() ?? string.Empty,
                recipientName,
                candidate.CreatedAtUtc,
                candidate.CreatedAtUtc.AddDays(rule.Days),
                ReminderDispatchStatus.Pending,
                $"debt:{candidate.Id:D}",
                nowUtc);

            await EnqueueAutomatedEmailAsync(
                log,
                "Ödeme hatırlatması",
                $"Sayın {recipientName},\n{clinicName} kliniğindeki hesabınızda ödenmemiş bakiye görünüyor. Ayrıntı için kliniğinizle görüşebilirsiniz. Ödemenizi yaptıysanız bu iletiyi dikkate almayın.{OptOutFooter}",
                dailyBudget,
                ct);
        }
    }

    /// <summary>
    /// Son gelişin üzerinden N gün geçen, planlı randevusu olmayan hayvan. Ticari ileti sayılır (ADR-046): yalnız kliniğin ticari ileti
    /// kapısı açıkken (<see cref="ProcessAutomatedRuleAsync"/>) ve yalnız bu klinikte aktif portal bağında ticari ileti onayı veren sahibe
    /// gider. Her iletide konu satırında nitelik, kliniğin adı ve ret bağlantısı bulunur (<see cref="NoReturnMarketingEmail"/>).
    /// Son geliş kiracı genelindedir: başka şubeye gelen hayvan "dönmedi" sayılmaz.
    /// </summary>
    private async Task ProcessNoReturnRuleAsync(
        ClinicReminderRule rule,
        DateTime nowUtc,
        int batchSize,
        DailyEmailBudget dailyBudget,
        CancellationToken ct)
    {
        if (rule.EnabledAtUtc is not { } enabledAtUtc)
            return;

        var tenantId = rule.TenantId;
        var clinicId = rule.ClinicId;
        var dueCutoffUtc = nowUtc.AddDays(-rule.Days);
        var floorUtc = enabledAtUtc.AddDays(-rule.Days);
        var eligiblePetIds = ReminderEligiblePetIds(tenantId);
        var consentedClientIds = _db.OwnerAccountClientLinks
            .Where(l => l.TenantId == tenantId
                && l.ClinicId == clinicId
                && l.Status == OwnerAccountClientLinkStatus.Active
                && l.MarketingConsentAtUtc != null)
            .Select(l => l.ClientId);

        var candidates = await _db.Visits
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId
                && v.ClinicId == clinicId
                && v.VoidedAtUtc == null
                && v.ArrivedAtUtc <= dueCutoffUtc
                && v.ArrivedAtUtc >= floorUtc
                && eligiblePetIds.Contains(v.PetId)
                && _db.Pets.Any(p => p.Id == v.PetId && consentedClientIds.Contains(p.ClientId))
                && !_db.Visits.Any(o => o.TenantId == tenantId && o.PetId == v.PetId && o.VoidedAtUtc == null && o.ArrivedAtUtc > v.ArrivedAtUtc)
                && !_db.Appointments.Any(a => a.TenantId == tenantId
                    && a.PetId == v.PetId
                    && a.Status == AppointmentStatus.Scheduled
                    && a.ScheduledAtUtc > nowUtc)
                && !_db.ReminderDispatchLogs.Any(l => l.TenantId == tenantId
                    && l.SourceEntityType == ReminderSourceEntityType.Pet
                    && l.SourceEntityId == v.PetId
                    && l.ScheduledForUtc == v.ArrivedAtUtc))
            .OrderBy(v => v.ArrivedAtUtc)
            .Select(v => new { v.PetId, v.ArrivedAtUtc })
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var recipient = await LoadPetRecipientAsync(tenantId, candidate.PetId, ct);
            var clinic = await _db.Clinics
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == clinicId)
                .Select(c => new { c.Name, c.Phone })
                .FirstOrDefaultAsync(ct);
            var consentedLinkId = await _db.OwnerAccountClientLinks
                .AsNoTracking()
                .Where(l => l.TenantId == tenantId
                    && l.ClinicId == clinicId
                    && l.Status == OwnerAccountClientLinkStatus.Active
                    && l.MarketingConsentAtUtc != null
                    && _db.Pets.Any(p => p.Id == candidate.PetId && p.ClientId == l.ClientId))
                .Select(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (consentedLinkId == Guid.Empty)
                continue;

            var optOut = _optOutLinks.Issue(tenantId, clinicId, consentedLinkId, nowUtc);
            var clinicName = clinic?.Name ?? "Klinik";
            var log = new ReminderDispatchLog(
                tenantId,
                clinicId,
                ReminderType.NoReturn,
                ReminderSourceEntityType.Pet,
                candidate.PetId,
                recipient.Email,
                recipient.Name,
                candidate.ArrivedAtUtc,
                candidate.ArrivedAtUtc.AddDays(rule.Days),
                ReminderDispatchStatus.Pending,
                $"noreturn:{candidate.PetId:D}:{candidate.ArrivedAtUtc:yyyyMMdd}",
                nowUtc);

            await EnqueueAutomatedEmailAsync(
                log,
                NoReturnMarketingEmail.Subject(clinicName),
                NoReturnMarketingEmail.Body(
                    recipient.Name, recipient.PetName, clinicName, FormatDateForDisplay(candidate.ArrivedAtUtc), clinic?.Phone, optOut.Url),
                dailyBudget,
                ct,
                optOut.Link);
        }
    }

    private async Task EnqueueAutomatedEmailAsync(
        ReminderDispatchLog log,
        string subject,
        string body,
        DailyEmailBudget dailyBudget,
        CancellationToken ct,
        MarketingOptOutLink? optOutLink = null)
    {
        if (string.IsNullOrWhiteSpace(log.RecipientEmail))
        {
            log.MarkSkipped("Recipient email is missing", log.CreatedAtUtc);
            await TryInsertLogAsync(log, ct);
            return;
        }

        if (!dailyBudget.TryConsume())
        {
            log.MarkSkipped(TrialLimits.ReminderDailyLimitSkipReason, log.CreatedAtUtc);
            await TryInsertLogAsync(log, ct);
            return;
        }

        try
        {
            var outboxMessageId = await _reminderEmailOutboxEnqueuer.EnqueueReminderEmailAsync(log.RecipientEmail, subject, body, isHtml: false, ct);
            log.MarkEnqueued(log.CreatedAtUtc, outboxMessageId);
            if (optOutLink is not null)
                _db.MarketingOptOutLinks.Add(optOutLink);
        }
        catch (Exception ex)
        {
            log.MarkFailed(ex.Message, log.CreatedAtUtc);
            _logger.LogWarning(ex, "Automated reminder enqueue failed. Tenant={TenantId} DedupeKey={DedupeKey}", log.TenantId, log.DedupeKey);
        }

        await TryInsertLogAsync(log, ct, detachOutboxMessageIdOnConflict: log.OutboxMessageId, detachLinksOnConflict: optOutLink is null ? null : [optOutLink]);
    }

    private async Task<(string Email, string Name, string PetName)> LoadPetRecipientAsync(Guid tenantId, Guid petId, CancellationToken ct)
    {
        var row = await _db.Pets
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Id == petId)
            .Join(_db.Clients.AsNoTracking(), p => p.ClientId, c => c.Id, (p, c) => new { PetName = p.Name, c.FullName, c.Email })
            .FirstOrDefaultAsync(ct);

        return (
            row?.Email?.Trim().ToLowerInvariant() ?? string.Empty,
            string.IsNullOrWhiteSpace(row?.FullName) ? "Müşterimiz" : row!.FullName.Trim(),
            string.IsNullOrWhiteSpace(row?.PetName) ? "Dostunuz" : row!.PetName.Trim());
    }

    private async Task<string> LoadClinicNameAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
        => await _db.Clinics
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == clinicId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct) ?? "Klinik";

    private static string FormatDateForDisplay(DateTime utcDateTime) => FormatForDisplay(utcDateTime)[..10];
}
