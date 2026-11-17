using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Commands.ApplyOwnerUnit;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Commands.RollbackBalanceAdjustments;

/// <summary>
/// Geri almanın son adımı (ADR-051, IMPORT_API_CONTRACT.md §14.5): partinin <b>eşleşen</b> müşterilere açtığı bakiye farkı
/// hareketlerinden değişmemiş olanları siler; tahsis ya da mahsup görmüş olan kalır. Eşleşen müşteriye dokunulmaz. Sahip birimleri
/// bittikten sonra arka plan geri alma işçisinden, partinin kiracı kapsamında gönderilir; tekrar çalıştırılabilir (silinen yeniden
/// bulunmaz). Kira kaybında birimin yazımları geri alınır.
/// </summary>
public sealed record RollbackImportBalanceAdjustmentsCommand(Guid BatchId, string LeaseOwner)
    : IRequest<Result<ImportBalanceAdjustmentRollbackResult>>, ITransactionalRollbackOnFailureRequest;

public sealed record ImportBalanceAdjustmentRollbackResult(int Deleted, int Kept);

public sealed class RollbackImportBalanceAdjustmentsCommandHandler
    : IRequestHandler<RollbackImportBalanceAdjustmentsCommand, Result<ImportBalanceAdjustmentRollbackResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ImportBatch> _batches;
    private readonly IRepository<Charge> _charges;
    private readonly IRepository<ClientCredit> _credits;
    private readonly IImportRollbackDependencyReader _dependencies;
    private readonly TimeProvider _timeProvider;

    public RollbackImportBalanceAdjustmentsCommandHandler(
        ITenantContext tenantContext,
        IRepository<ImportBatch> batches,
        IRepository<Charge> charges,
        IRepository<ClientCredit> credits,
        IImportRollbackDependencyReader dependencies,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _batches = batches;
        _charges = charges;
        _credits = credits;
        _dependencies = dependencies;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ImportBalanceAdjustmentRollbackResult>> Handle(
        RollbackImportBalanceAdjustmentsCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ImportBalanceAdjustmentRollbackResult>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.BatchId, tracking: true), ct);
        if (batch is null || !batch.RenewRollbackLease(request.LeaseOwner, _timeProvider.GetUtcNow().UtcDateTime).IsSuccess)
            return Result<ImportBalanceAdjustmentRollbackResult>.Failure(ImportOwnerUnitErrors.LeaseLost());

        var facts = await _dependencies.ReadBalanceAdjustmentsAsync(tenantId, batch.Id, ct);
        var unchanged = facts.Where(f => !f.Changed).ToList();

        var chargeIds = unchanged.Where(f => !f.IsCredit).Select(f => f.Id).ToArray();
        if (chargeIds.Length > 0)
            await _charges.DeleteRangeAsync(await _charges.ListAsync(new ImportRollbackOpeningBalancesSpec(tenantId, batch.Id, chargeIds), ct), ct);

        var creditIds = unchanged.Where(f => f.IsCredit).Select(f => f.Id).ToArray();
        if (creditIds.Length > 0)
            await _credits.DeleteRangeAsync(await _credits.ListAsync(new ImportRollbackOpeningCreditsSpec(tenantId, batch.Id, creditIds), ct), ct);

        // Silmeler + kira yenilemesi aynı işlemde; parti devralındıysa rowversion çakışır.
        await _batches.SaveChangesAsync(ct);
        return Result<ImportBalanceAdjustmentRollbackResult>.Success(
            new ImportBalanceAdjustmentRollbackResult(unchanged.Count, facts.Count - unchanged.Count));
    }
}
