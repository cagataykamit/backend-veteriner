using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Vaccinations.Access;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.Commit;

/// <summary>
/// Commit isteği (IMPORT_API_CONTRACT.md §7.9). Kullanıcıya bağlı tüm denetimler (ek izinler, klinik yazma erişimi,
/// önizleme kuralı, <c>onErrors</c>) burada yapılır; arka plan işi kullanıcı bağlamı olmadan partinin kiracısı adına
/// çalışır. Parti <c>Committing</c> olunca iş kuyruğa girmiş sayılır (iş kuyruğu = parti tablosu).
/// </summary>
public sealed class CommitImportBatchCommandHandler : IRequestHandler<CommitImportBatchCommand, Result<ImportCommitResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<ImportBatch> _batches;
    private readonly IReadRepository<ImportBatchRow> _rows;
    private readonly ImportTableInfoLoader _tableInfo;
    private readonly TimeProvider _timeProvider;

    public CommitImportBatchCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Tenant> tenants,
        IRepository<ImportBatch> batches,
        IReadRepository<ImportBatchRow> rows,
        ImportTableInfoLoader tableInfo,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _permissions = permissions;
        _tenants = tenants;
        _batches = batches;
        _rows = rows;
        _tableInfo = tableInfo;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ImportCommitResultDto>> Handle(CommitImportBatchCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id, tracking: true), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportCommitResultDto>.Failure(ImportBatchVisibility.NotFound());

        // Tekrar: aynı doğrulamanın commit'i zaten alınmış; yeni iş başlamaz.
        if (IsReplay(batch, request.ValidationId))
            return await ReplayAsync(batch, ct);

        if (batch.Status == ImportBatchStatus.Validated && batch.ValidationId != request.ValidationId)
            return Fail("Imports.ValidationStale", "Önizleme güncel değil; eşleme değişmiş veya parti yeniden doğrulanmış.");
        if (batch.Status != ImportBatchStatus.Validated)
            return Fail("Imports.InvalidState", $"Parti '{batch.Status}' durumundayken uygulanamaz.");

        var permission = ImportWritePermissions.Check(batch, _permissions);
        if (!permission.IsSuccess)
            return Result<ImportCommitResultDto>.Failure(permission.Error);

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracıda içe aktarma uygulanamaz.");

        var clinic = await CheckClinicAsync(batch, tenantId, ct);
        if (!clinic.IsSuccess)
            return Result<ImportCommitResultDto>.Failure(clinic.Error);

        var issuesJson = await _rows.ListAsync(new ImportIssuesJsonSpec(batch.Id), ct);
        var (unmapped, _) = ImportPreviewAggregator.AggregateIssues(issuesJson);
        var blockers = ImportCommitGate.Blockers(batch, unmapped);
        if (!ImportCommitGate.CanCommit(batch, blockers))
        {
            if (blockers.Count == 0)
                blockers.Add(ImportRules.NoImportableRows.Code);
            return Result<ImportCommitResultDto>.Failure(new Error(
                "Imports.CommitBlocked",
                "Parti uygulanamaz: önizlemedeki engelleri giderin (eşlenmemiş değer, bağlantı sütunu veya oluşturulacak kayıt yok).",
                new Dictionary<string, object?> { ["blockers"] = blockers }));
        }

        if (request.OnErrors == CommitImportBatchCommand.FailIfAny && ImportCommitGate.ErrorRows(batch) > 0)
        {
            return Fail(
                "Imports.HasErrorRows",
                $"{ImportCommitGate.ErrorRows(batch)} hatalı satır var; 'FailIfAny' seçiminde hiçbir şey uygulanmaz.");
        }

        var started = batch.StartCommit(request.ValidationId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!started.IsSuccess)
            return Result<ImportCommitResultDto>.Failure(started.Error);

        try
        {
            await _batches.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı ikinci istek: diğeri aynı doğrulamayı commit ettiyse bu bir tekrardır.
            var current = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id), ct);
            if (current is not null && IsReplay(current, request.ValidationId))
                return await ReplayAsync(current, ct);
            return Fail("Imports.ConcurrencyConflict", "Parti eşzamanlı olarak değişti; durumu yeniden okuyup tekrar deneyin.");
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains(ImportBatch.ActiveJobIndexName, StringComparison.Ordinal))
        {
            return Fail("Imports.AnotherJobRunning", "Kiracıda başka bir içe aktarma işi çalışıyor; bitmesini bekleyin.");
        }

        return Result<ImportCommitResultDto>.Success(
            new ImportCommitResultDto(Started: true, batch.ToDto(await _tableInfo.LoadAsync(batch, ct))));
    }

    private static bool IsReplay(ImportBatch batch, Guid validationId)
        => batch.Status is ImportBatchStatus.Committing or ImportBatchStatus.Committed
           && batch.ValidationId == validationId;

    private async Task<Result<ImportCommitResultDto>> ReplayAsync(ImportBatch batch, CancellationToken ct)
        => Result<ImportCommitResultDto>.Success(
            new ImportCommitResultDto(Started: false, batch.ToDto(await _tableInfo.LoadAsync(batch, ct))));

    /// <summary>Aşı tablosu varsa partinin kliniğine yazma erişimi (günlük aşı girişiyle aynı kural).</summary>
    private async Task<Result> CheckClinicAsync(ImportBatch batch, Guid tenantId, CancellationToken ct)
    {
        if (batch.VaccinationCounts.Total == 0)
            return Result.Success();

        if (batch.ClinicId is not { } clinicId)
            return Result.Failure("Imports.ClinicRequired", "Aşı tablosu içeren partide klinik zorunludur.");

        if (_clinicContext.ClinicId is { } active && active != clinicId)
            return Result.Failure("Imports.ClinicContextMismatch", "Partinin kliniği aktif klinik bağlamıyla uyuşmuyor.");

        return await VaccinationClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, clinicId, ct);
    }

    private static Result<ImportCommitResultDto> Fail(string code, string message)
        => Result<ImportCommitResultDto>.Failure(code, message);
}
