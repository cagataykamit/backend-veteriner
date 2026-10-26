using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Queries.GetProgress;

/// <summary>GET /api/v1/imports/batches/{id}/progress (IMPORT_API_CONTRACT.md §7.14): yoklama için hafif durum.</summary>
public sealed record GetImportBatchProgressQuery(Guid Id) : IRequest<Result<ImportProgressDto>>;

/// <summary>
/// En çok üç sorgu: parti + doğrulama fazında sonucu yazılmış satır sayısı, uygulama sonucu yazılmış satır sayısı
/// (uygulama başladıysa) ya da geri alma fazında hedef ve sonucu yazılmış satır sayıları.
/// </summary>
public sealed class GetImportBatchProgressQueryHandler : IRequestHandler<GetImportBatchProgressQuery, Result<ImportProgressDto>>
{
    private static readonly ImportBatchStatus[] RollbackPhase =
    [
        ImportBatchStatus.RollingBack, ImportBatchStatus.RolledBack,
        ImportBatchStatus.PartiallyRolledBack, ImportBatchStatus.RollbackFailed,
    ];

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImportBatch> _batches;
    private readonly IReadRepository<ImportBatchRow> _rows;
    private readonly TimeProvider _timeProvider;

    public GetImportBatchProgressQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ImportBatch> batches,
        IReadRepository<ImportBatchRow> rows,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _rows = rows;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ImportProgressDto>> Handle(GetImportBatchProgressQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ImportProgressDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportProgressDto>.Failure(ImportBatchVisibility.NotFound());

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var leaseActive = batch.IsJobLeaseActive(now);
        int total, processed;
        if (batch.Status == ImportBatchStatus.Validating)
        {
            // Doğrulama: işçi kirayı alıp önceki sonuçları sıfırladıktan sonra sonucu yazılmış satırlar; kuyruktayken 0.
            total = batch.CustomerCounts.Total + batch.PetCounts.Total + batch.VaccinationCounts.Total;
            processed = leaseActive ? await _rows.CountAsync(new ImportValidatedRowsSpec(batch.Id), ct) : 0;
        }
        else if (RollbackPhase.Contains(batch.Status))
        {
            // Geri alma: hedef satırlar (oluşturulan kaydı taşıyan) ve sonucu yazılmış olanlar.
            processed = await _rows.CountAsync(
                new ImportRowsByApplyStatusSpec(batch.Id, ImportRowApplyStatus.RolledBack, ImportRowApplyStatus.Kept), ct);
            total = processed + await _rows.CountAsync(new ImportRowsByApplyStatusSpec(batch.Id, ImportRowApplyStatus.Created), ct);
        }
        else
        {
            total = batch.CustomerCounts.Total + batch.PetCounts.Total + batch.VaccinationCounts.Total;
            var started = batch.Status is ImportBatchStatus.Committing or ImportBatchStatus.Committed or ImportBatchStatus.CommitFailed;
            processed = started ? await _rows.CountAsync(new ImportAppliedRowsSpec(batch.Id), ct) : 0;
        }

        var phase = batch.Status switch
        {
            ImportBatchStatus.Validating => nameof(ImportBatchStatus.Validating),
            ImportBatchStatus.Committing => nameof(ImportBatchStatus.Committing),
            ImportBatchStatus.RollingBack => nameof(ImportBatchStatus.RollingBack),
            _ => null,
        };

        return Result<ImportProgressDto>.Success(new ImportProgressDto(
            batch.Id,
            batch.Status,
            phase,
            processed,
            total,
            batch.UpdatedAtUtc,
            batch.CommittedAtUtc,
            batch.RolledBackAtUtc,
            leaseActive));
    }
}
