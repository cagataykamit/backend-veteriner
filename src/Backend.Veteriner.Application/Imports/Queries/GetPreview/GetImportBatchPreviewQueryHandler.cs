using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Application.Reminders.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetPreview;

/// <summary>
/// Kuru çalıştırma önizlemesi (IMPORT_API_CONTRACT.md §7.6). Sayaçlar partiden, eşlenemeyen değerler/sorun sayıları/
/// hatırlatma etkisi saklanan satır sonuçlarından türetilir (en çok iki ek sorgu); dosya düzeyi engeller eşleme ve
/// tablo varlığından yeniden hesaplanır.
/// </summary>
public sealed class GetImportBatchPreviewQueryHandler : IRequestHandler<GetImportBatchPreviewQuery, Result<ImportPreviewDto>>
{
    private const int DefaultReminderWindowDays = 3;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImportBatch> _batches;
    private readonly IReadRepository<ImportBatchRow> _rows;
    private readonly IReadRepository<TenantReminderSettings> _reminderSettings;
    private readonly TimeProvider _timeProvider;

    public GetImportBatchPreviewQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ImportBatch> batches,
        IReadRepository<ImportBatchRow> rows,
        IReadRepository<TenantReminderSettings> reminderSettings,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _rows = rows;
        _reminderSettings = reminderSettings;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ImportPreviewDto>> Handle(GetImportBatchPreviewQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ImportPreviewDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportPreviewDto>.Failure(ImportBatchVisibility.NotFound());

        if (!batch.HasValidationResult)
        {
            return Result<ImportPreviewDto>.Failure(
                "Imports.InvalidState",
                $"Parti '{batch.Status}' durumundayken önizleme yok; önce doğrulayın.");
        }

        var summary = batch.ToDto().Summary;
        var present = ImportTableInfoLoader.PresentTables(batch);
        var blockers = ImportPreconditions.Check(ImportMappingDocument.Parse(batch.MappingJson), present)
            .Select(r => r.Code)
            .ToList();

        var settings = await _reminderSettings.FirstOrDefaultAsync(new TenantReminderSettingsByTenantSpec(tenantId), ct);
        var windowDays = settings?.VaccinationReminderDaysBefore ?? DefaultReminderWindowDays;
        var remindersEnabled = settings?.VaccinationRemindersEnabled ?? false;

        if (batch.PayloadPurgedAtUtc is not null)
        {
            return Result<ImportPreviewDto>.Success(new ImportPreviewDto(
                batch.ValidationId, batch.Status, summary, [],
                ImportPreviewAggregator.ReminderImpact([], _timeProvider.GetUtcNow().UtcDateTime, windowDays, remindersEnabled),
                [], false, blockers, PayloadPurged: true));
        }

        var issuesJson = await _rows.ListAsync(new ImportIssuesJsonSpec(batch.Id), ct);
        var (unmapped, issueCounts) = ImportPreviewAggregator.AggregateIssues(issuesJson);
        blockers.AddRange(ImportPreviewAggregator.BlockingCodes(unmapped));

        var accepted = new[] { summary.Customers, summary.Pets, summary.Vaccinations }.Sum(c => c.Valid + c.Warning);
        var total = new[] { summary.Customers, summary.Pets, summary.Vaccinations }.Sum(c => c.Total);
        if (blockers.Count == 0 && accepted == 0 && total > 0)
            blockers.Add(ImportRules.NoImportableRows.Code);

        var vaccinationJson = summary.Vaccinations.Total > 0
            ? await _rows.ListAsync(new ImportVaccinationNormalizedJsonSpec(batch.Id), ct)
            : [];
        var impact = ImportPreviewAggregator.ReminderImpact(
            vaccinationJson, _timeProvider.GetUtcNow().UtcDateTime, windowDays, remindersEnabled);

        var willCreate = summary.Customers.WillCreate + summary.Pets.WillCreate + summary.Vaccinations.WillCreate;
        var canCommit = batch.Status == ImportBatchStatus.Validated
                        && batch.ValidationId is not null
                        && blockers.Count == 0
                        && willCreate > 0;

        return Result<ImportPreviewDto>.Success(new ImportPreviewDto(
            batch.ValidationId, batch.Status, summary, unmapped, impact, issueCounts, canCommit, blockers, PayloadPurged: false));
    }
}
