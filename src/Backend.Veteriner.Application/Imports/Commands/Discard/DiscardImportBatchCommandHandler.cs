using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.Discard;

/// <summary>
/// Taslağı siler (durum <c>Discarded</c>). Hazırlık satırları (kişisel veri) partiyle aynı kayıt işleminde silinir;
/// ürün verisine dokunulmaz.
/// </summary>
public sealed class DiscardImportBatchCommandHandler : IRequestHandler<DiscardImportBatchCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ImportBatch> _batches;
    private readonly IRepository<ImportBatchRow> _rows;
    private readonly TimeProvider _timeProvider;

    public DiscardImportBatchCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ImportBatch> batches,
        IRepository<ImportBatchRow> rows,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _rows = rows;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(DiscardImportBatchCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id, tracking: true), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result.Failure(ImportBatchVisibility.NotFound());

        var discarded = batch.Discard(_timeProvider.GetUtcNow().UtcDateTime);
        if (!discarded.IsSuccess)
            return discarded;

        try
        {
            // Satır silme ve izlenen parti değişikliği (Discarded) tek SaveChanges'te, tek işlemde yazılır.
            await _rows.DeleteRangeAsync(new ImportBatchRowsByBatchSpec(batch.Id), ct);
            await _rows.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Imports.ConcurrencyConflict",
                "Parti eşzamanlı olarak değişti; durumu yeniden okuyup tekrar deneyin.");
        }

        return Result.Success();
    }
}
