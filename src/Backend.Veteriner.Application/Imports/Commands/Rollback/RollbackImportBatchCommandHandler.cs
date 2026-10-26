using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.Rollback;

/// <summary>
/// Geri alma isteği (IMPORT_API_CONTRACT.md §7.12). Kullanıcıya bağlı denetimler (ek izinler, kiracı) ve <c>Fail</c>
/// politikasının engel denetimi burada yapılır; silme arka plan işindedir (sahip birimleri, aynı kuralla yeniden denetim).
/// İstek anahtarı ve politika parti seçeneklerine yazılır (iş politikayı oradan okur; tekrar istek buradan tanınır).
/// </summary>
public sealed class RollbackImportBatchCommandHandler
    : IRequestHandler<RollbackImportBatchCommand, Result<ImportRollbackResultDto>>
{
    public const int MaxBlockedItemsInError = 50;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<ImportBatch> _batches;
    private readonly ImportRollbackPlanLoader _plans;
    private readonly ImportTableInfoLoader _tableInfo;
    private readonly TimeProvider _timeProvider;

    public RollbackImportBatchCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Tenant> tenants,
        IRepository<ImportBatch> batches,
        ImportRollbackPlanLoader plans,
        ImportTableInfoLoader tableInfo,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _permissions = permissions;
        _tenants = tenants;
        _batches = batches;
        _plans = plans;
        _tableInfo = tableInfo;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ImportRollbackResultDto>> Handle(RollbackImportBatchCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id, tracking: true), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportRollbackResultDto>.Failure(ImportBatchVisibility.NotFound());

        if (await ReplayAsync(batch, request, ct) is { } replay)
            return replay;

        if (!batch.CanStartRollback)
            return Fail("Imports.InvalidState", $"Parti '{batch.Status}' durumundayken geri alınamaz.");

        var permission = ImportWritePermissions.Check(batch, _permissions);
        if (!permission.IsSuccess)
            return Result<ImportRollbackResultDto>.Failure(permission.Error);

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracıda içe aktarma geri alınamaz.");

        var policy = request.EffectivePolicy;
        var plan = (await _plans.LoadAsync(tenantId, batch.Id, rowIds: null, ct)).Plan;
        var blocked = plan.Total(ImportRollbackDecision.Keep);
        if (blocked > 0 && (policy == ImportRollbackPolicy.Fail || plan.Total(ImportRollbackDecision.Delete) == 0))
            return Result<ImportRollbackResultDto>.Failure(BlockedError(plan, policy));

        var options = ImportOptionsDocument.Parse(batch.OptionsJson) with
        {
            Rollback = new ImportRollbackRequest(request.RequestId, policy),
        };
        var started = batch.StartRollback(options.ToJson(), _timeProvider.GetUtcNow().UtcDateTime);
        if (!started.IsSuccess)
            return Result<ImportRollbackResultDto>.Failure(started.Error);

        try
        {
            await _batches.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı ikinci istek: diğeri aynı requestId ile başlattıysa bu bir tekrardır.
            var current = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id), ct);
            if (current is not null && await ReplayAsync(current, request, ct) is { } concurrentReplay)
                return concurrentReplay;
            return Fail("Imports.ConcurrencyConflict", "Parti eşzamanlı olarak değişti; durumu yeniden okuyup tekrar deneyin.");
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains(ImportBatch.ActiveJobIndexName, StringComparison.Ordinal))
        {
            return Fail("Imports.AnotherJobRunning", "Kiracıda başka bir içe aktarma işi çalışıyor; bitmesini bekleyin.");
        }

        return Result<ImportRollbackResultDto>.Success(
            new ImportRollbackResultDto(Started: true, batch.ToDto(await _tableInfo.LoadAsync(batch, ct))));
    }

    /// <summary>
    /// Partinin son geri alma isteği bu <c>requestId</c> ise yeni iş başlamaz: aynı politika <c>200</c> + parti, farklı
    /// politika <c>409 Imports.RequestIdConflict</c>. Tekrar değilse null.
    /// </summary>
    private async Task<Result<ImportRollbackResultDto>?> ReplayAsync(
        ImportBatch batch, RollbackImportBatchCommand request, CancellationToken ct)
    {
        if (batch.Status is not (ImportBatchStatus.RollingBack or ImportBatchStatus.RolledBack
                or ImportBatchStatus.PartiallyRolledBack or ImportBatchStatus.RollbackFailed))
            return null;

        var previous = ImportOptionsDocument.Parse(batch.OptionsJson).Rollback;
        if (previous is null || previous.RequestId != request.RequestId)
            return null;

        if (previous.Policy != request.EffectivePolicy)
            return Fail("Imports.RequestIdConflict", "Aynı requestId farklı blockedPolicy ile kullanılmış.");

        return Result<ImportRollbackResultDto>.Success(
            new ImportRollbackResultDto(Started: false, batch.ToDto(await _tableInfo.LoadAsync(batch, ct))));
    }

    private static Error BlockedError(ImportRollbackPlan plan, string policy)
    {
        var message = policy == ImportRollbackPolicy.Fail
            ? "Bağlı kaydı olan veya değiştirilmiş kayıtlar var; 'Fail' politikasında hiçbir şey silinmez."
            : "Silinebilecek kayıt yok; kalan kayıtların hepsinin bağı var.";
        return new Error(
            "Imports.RollbackBlocked",
            message,
            new Dictionary<string, object?>
            {
                ["blocked"] = ImportRollbackPlanLoader.Counts(plan, ImportRollbackDecision.Keep),
                ["blockedItems"] = ImportRollbackPlanLoader.BlockedItems(plan, MaxBlockedItemsInError),
            });
    }

    private static Result<ImportRollbackResultDto> Fail(string code, string message)
        => Result<ImportRollbackResultDto>.Failure(code, message);
}
