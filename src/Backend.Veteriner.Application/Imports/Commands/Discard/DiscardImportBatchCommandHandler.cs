using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.Discard;

/// <summary>
/// Taslağı siler (durum <c>Discarded</c>). B1'de parti satırsızdır; hazırlık satırlarının silinmesi satırları yazan B2 ile gelir.
/// </summary>
public sealed class DiscardImportBatchCommandHandler : IRequestHandler<DiscardImportBatchCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ImportBatch> _batches;
    private readonly TimeProvider _timeProvider;

    public DiscardImportBatchCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ImportBatch> batches,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(DiscardImportBatchCommand request, CancellationToken ct)
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
            await _batches.SaveChangesAsync(ct);
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
