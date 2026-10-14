using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetPreview;

/// <summary>
/// Kuru çalıştırma önizlemesi (IMPORT_API_CONTRACT.md §7.6). Sayaçlar partiden, eşlenemeyen değerler/sorun sayıları
/// saklanan satır sonuçlarından türetilir; hatırlatma etkisi uygulama öncesi normalize satırlardan, sonrası gerçek
/// kayıtlardan (<see cref="ImportReminderImpactReader"/>); engeller ve <c>canCommit</c> <see cref="ImportCommitGate"/>.
/// </summary>
public sealed class GetImportBatchPreviewQueryHandler : IRequestHandler<GetImportBatchPreviewQuery, Result<ImportPreviewDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImportBatch> _batches;
    private readonly IReadRepository<ImportBatchRow> _rows;
    private readonly ImportReminderImpactReader _reminderImpact;
    private readonly TimeProvider _timeProvider;

    public GetImportBatchPreviewQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ImportBatch> batches,
        IReadRepository<ImportBatchRow> rows,
        ImportReminderImpactReader reminderImpact,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _rows = rows;
        _reminderImpact = reminderImpact;
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
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Uygulama başladıysa etki gerçek kayıtlardan (satır verisi temizlenmiş olsa da) hesaplanır.
        var impact = await _reminderImpact.ReadAsync(batch, now, ct);

        if (batch.PayloadPurgedAtUtc is not null)
        {
            return Result<ImportPreviewDto>.Success(new ImportPreviewDto(
                batch.ValidationId, batch.Status, summary, [], impact,
                [], false, ImportCommitGate.FileBlockers(batch), PayloadPurged: true));
        }

        var issuesJson = await _rows.ListAsync(new ImportIssuesJsonSpec(batch.Id), ct);
        var (unmapped, issueCounts) = ImportPreviewAggregator.AggregateIssues(issuesJson);
        var blockers = ImportCommitGate.Blockers(batch, unmapped);
        var canCommit = ImportCommitGate.CanCommit(batch, blockers);

        return Result<ImportPreviewDto>.Success(new ImportPreviewDto(
            batch.ValidationId, batch.Status, summary, unmapped, impact, issueCounts, canCommit, blockers, PayloadPurged: false));
    }
}
