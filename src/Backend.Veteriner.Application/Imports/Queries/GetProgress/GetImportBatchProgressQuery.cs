using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetProgress;

/// <summary>GET /api/v1/imports/batches/{id}/progress (IMPORT_API_CONTRACT.md §7.14): yoklama için hafif durum.</summary>
public sealed record GetImportBatchProgressQuery(Guid Id) : IRequest<Result<ImportProgressDto>>;

/// <summary>En çok iki sorgu: parti + uygulama sonucu yazılmış satır sayısı (yalnız uygulama başladıysa).</summary>
public sealed class GetImportBatchProgressQueryHandler : IRequestHandler<GetImportBatchProgressQuery, Result<ImportProgressDto>>
{
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

    public async Task<Result<ImportProgressDto>> Handle(GetImportBatchProgressQuery request, CancellationToken ct)
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

        var total = batch.CustomerCounts.Total + batch.PetCounts.Total + batch.VaccinationCounts.Total;
        var started = batch.Status is not (ImportBatchStatus.Uploaded or ImportBatchStatus.Mapped or ImportBatchStatus.Validating
            or ImportBatchStatus.Validated or ImportBatchStatus.ValidationFailed or ImportBatchStatus.Discarded or ImportBatchStatus.Expired);
        var processed = started ? await _rows.CountAsync(new ImportAppliedRowsSpec(batch.Id), ct) : 0;
        var committing = batch.Status == ImportBatchStatus.Committing;

        return Result<ImportProgressDto>.Success(new ImportProgressDto(
            batch.Id,
            batch.Status,
            committing ? nameof(ImportBatchStatus.Committing) : null,
            processed,
            total,
            batch.UpdatedAtUtc,
            batch.CommittedAtUtc,
            batch.IsCommitLeaseActive(_timeProvider.GetUtcNow().UtcDateTime)));
    }
}
