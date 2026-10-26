using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.ReleaseReminders;

/// <summary>
/// Hatırlatma serbest bırakma. Etki gerçek içe aktarılmış aşılardan hesaplanır (önizlemedeki sayıyla aynı kaynak);
/// onaylanan sayı tutmazsa güncel sayı döner. Zaten serbest bırakılmış parti için işlem tekrar etmez (idempotent).
/// E-posta hemen gitmez: hatırlatma işçisinin sonraki turunda mevcut pencere kuralıyla değerlendirilir.
/// </summary>
public sealed class ReleaseImportRemindersCommandHandler
    : IRequestHandler<ReleaseImportRemindersCommand, Result<ImportReleaseRemindersResultDto>>
{
    private const string RemindersDisabledNote = "Kiracının aşı hatırlatma ayarı kapalı; ayar açılana kadar e-posta gitmez.";

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ImportBatch> _batches;
    private readonly ImportReminderImpactReader _reminderImpact;
    private readonly TimeProvider _timeProvider;

    public ReleaseImportRemindersCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ImportBatch> batches,
        ImportReminderImpactReader reminderImpact,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _reminderImpact = reminderImpact;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ImportReleaseRemindersResultDto>> Handle(ReleaseImportRemindersCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ImportReleaseRemindersResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id, tracking: true), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportReleaseRemindersResultDto>.Failure(ImportBatchVisibility.NotFound());

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (batch.RemindersReleasedAtUtc is not null)
            return await ResultAsync(batch, now, alreadyReleased: true, ct);

        if (batch.Status is not (ImportBatchStatus.Committed or ImportBatchStatus.PartiallyRolledBack))
        {
            return Result<ImportReleaseRemindersResultDto>.Failure(
                "Imports.InvalidState",
                $"Parti '{batch.Status}' durumundayken hatırlatmalar serbest bırakılamaz; önce uygulama bitmelidir.");
        }

        var impact = await _reminderImpact.ReadAsync(batch, now, ct);
        if (impact.EmailsWithinReminderWindow != request.ConfirmEmailCount)
        {
            return Result<ImportReleaseRemindersResultDto>.Failure(new Error(
                "Imports.ReminderImpactChanged",
                $"Onaylanan e-posta sayısı güncel değil; güncel sayı {impact.EmailsWithinReminderWindow}. Yeniden onaylayın.",
                new Dictionary<string, object?> { ["emailsWithinReminderWindow"] = impact.EmailsWithinReminderWindow }));
        }

        var released = batch.ReleaseReminders(now);
        if (!released.IsSuccess)
            return Result<ImportReleaseRemindersResultDto>.Failure(released.Error);

        try
        {
            await _batches.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ImportReleaseRemindersResultDto>.Failure(
                "Imports.ConcurrencyConflict",
                "Parti eşzamanlı olarak değişti; durumu yeniden okuyup tekrar deneyin.");
        }

        return Result<ImportReleaseRemindersResultDto>.Success(
            new ImportReleaseRemindersResultDto(batch.ToDto(), impact, AlreadyReleased: false, NoteFor(impact)));
    }

    private async Task<Result<ImportReleaseRemindersResultDto>> ResultAsync(
        ImportBatch batch, DateTime now, bool alreadyReleased, CancellationToken ct)
    {
        var impact = await _reminderImpact.ReadAsync(batch, now, ct);
        return Result<ImportReleaseRemindersResultDto>.Success(
            new ImportReleaseRemindersResultDto(batch.ToDto(), impact, alreadyReleased, NoteFor(impact)));
    }

    private static string? NoteFor(ImportReminderImpactDto impact)
        => impact.TenantRemindersEnabled ? null : RemindersDisabledNote;
}
