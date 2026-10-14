using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetRollbackPreview;

/// <summary>
/// GET /api/v1/imports/batches/{id}/rollback-preview (IMPORT_API_CONTRACT.md §7.11): hangi kayıtların silinebileceği,
/// hangilerinin neden kalacağı. Hiçbir şey yazmaz; kural geri alma işiyle aynıdır (<see cref="ImportRollbackPlanner"/>).
/// </summary>
public sealed record GetImportRollbackPreviewQuery(Guid Id) : IRequest<Result<ImportRollbackPreviewDto>>;

public sealed class GetImportRollbackPreviewQueryHandler
    : IRequestHandler<GetImportRollbackPreviewQuery, Result<ImportRollbackPreviewDto>>
{
    public const int MaxBlockedItems = 200;
    public const string PetEditsNotTracked = "Imports.PetEditsNotTracked";
    public const string RemindersAlreadyReleased = "Imports.RemindersAlreadyReleased";

    private static readonly ImportBatchStatus[] PreviewableFrom =
    [
        ImportBatchStatus.Committed, ImportBatchStatus.CommitFailed, ImportBatchStatus.RollingBack,
        ImportBatchStatus.RolledBack, ImportBatchStatus.PartiallyRolledBack, ImportBatchStatus.RollbackFailed,
    ];

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImportBatch> _batches;
    private readonly ImportRollbackPlanLoader _plans;

    public GetImportRollbackPreviewQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ImportBatch> batches,
        ImportRollbackPlanLoader plans)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _plans = plans;
    }

    public async Task<Result<ImportRollbackPreviewDto>> Handle(GetImportRollbackPreviewQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ImportRollbackPreviewDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportRollbackPreviewDto>.Failure(ImportBatchVisibility.NotFound());

        if (!PreviewableFrom.Contains(batch.Status))
        {
            return Result<ImportRollbackPreviewDto>.Failure(
                "Imports.InvalidState",
                $"Parti '{batch.Status}' durumundayken geri alma önizlemesi yok; parti uygulanmamış.");
        }

        var snapshot = await _plans.LoadAsync(tenantId, batch.Id, rowIds: null, ct);
        return Result<ImportRollbackPreviewDto>.Success(Build(batch, snapshot));
    }

    private static ImportRollbackPreviewDto Build(ImportBatch batch, ImportRollbackSnapshot snapshot)
    {
        var plan = snapshot.Plan;
        var deletable = ImportRollbackPlanLoader.Counts(plan, ImportRollbackDecision.Delete);
        var blocked = ImportRollbackPlanLoader.Counts(plan, ImportRollbackDecision.Keep);
        var rollbackable = batch.CanStartRollback;

        var warnings = new List<string>();
        if (deletable.Pets > 0)
            warnings.Add(PetEditsNotTracked);
        if (batch.RemindersReleasedAtUtc is not null)
            warnings.Add(RemindersAlreadyReleased);

        return new ImportRollbackPreviewDto(
            batch.Status,
            CanRollback: rollbackable && blocked.Total == 0,
            CanDeleteUnblockedOnly: rollbackable && blocked.Total > 0 && deletable.Total > 0,
            deletable,
            blocked,
            ImportRollbackPlanLoader.BlockedItems(plan, MaxBlockedItems),
            BlockedItemsTruncated: blocked.Total > MaxBlockedItems,
            new ImportRollbackKeptDto(
                snapshot.CountRows(ImportTable.Customers, ImportRowApplyStatus.Matched),
                snapshot.CountRows(ImportTable.Pets, ImportRowApplyStatus.Matched)),
            new ImportRollbackCountsDto(
                snapshot.CountRows(ImportTable.Customers, ImportRowApplyStatus.RolledBack),
                snapshot.CountRows(ImportTable.Pets, ImportRowApplyStatus.RolledBack),
                snapshot.CountRows(ImportTable.Vaccinations, ImportRowApplyStatus.RolledBack)),
            batch.RemindersReleasedAtUtc is not null,
            warnings);
    }
}
