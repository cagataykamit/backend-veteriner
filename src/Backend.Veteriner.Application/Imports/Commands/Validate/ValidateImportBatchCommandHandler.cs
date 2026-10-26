using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.Validate;

/// <summary>
/// Kuru çalıştırma isteği (IMPORT_API_CONTRACT.md §7.4, ADR-019 K13). İstek yalnız denetler ve kuyruğa alır: parti
/// <c>Validating</c> olarak kaydedilir (kiracıda tek aktif iş indeksi), satırları arka plan doğrulama işçisi işler. Parti
/// zaten <c>Validating</c> ise yeni iş başlamaz, aynı parti döner (tekrar; çift tıklama güvenli).
/// </summary>
public sealed class ValidateImportBatchCommandHandler
    : IRequestHandler<ValidateImportBatchCommand, Result<ImportValidationStartResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ImportBatch> _batches;
    private readonly ImportTableInfoLoader _tableInfo;
    private readonly TimeProvider _timeProvider;

    public ValidateImportBatchCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ImportBatch> batches,
        ImportTableInfoLoader tableInfo,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _tableInfo = tableInfo;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ImportValidationStartResultDto>> Handle(ValidateImportBatchCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id, tracking: true), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportValidationStartResultDto>.Failure(ImportBatchVisibility.NotFound());

        // Tekrar: doğrulama kuyrukta veya sürüyor (eşleme bu sırada değişemez, aynı iştir).
        if (batch.Status == ImportBatchStatus.Validating)
            return await ResultAsync(batch, started: false, ct);

        if (!batch.CanStartValidation)
            return Fail("Imports.InvalidState", $"Parti '{batch.Status}' durumundayken doğrulanamaz; önce eşleme kaydedin.");

        var mapping = ImportMappingDocument.Parse(batch.MappingJson);
        var missing = ImportPreconditions.MissingRequired(mapping, ImportTableInfoLoader.PresentTables(batch));
        if (missing.Count > 0)
            return Fail("Imports.MappingIncomplete", $"Eksik zorunlu alan eşlemesi — {string.Join("; ", missing)}.");

        var started = batch.StartValidation(_timeProvider.GetUtcNow().UtcDateTime);
        if (!started.IsSuccess)
            return Result<ImportValidationStartResultDto>.Failure(started.Error);

        try
        {
            await _batches.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı ikinci istek: diğeri doğrulamayı kuyruğa aldıysa bu bir tekrardır.
            var current = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id), ct);
            if (current?.Status == ImportBatchStatus.Validating)
                return await ResultAsync(current, started: false, ct);
            return Fail("Imports.ConcurrencyConflict", "Parti eşzamanlı olarak değişti; durumu yeniden okuyup tekrar deneyin.");
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains(ImportBatch.ActiveJobIndexName, StringComparison.Ordinal))
        {
            return Fail("Imports.AnotherJobRunning", "Kiracıda başka bir içe aktarma işi çalışıyor; bitmesini bekleyin.");
        }

        return await ResultAsync(batch, started: true, ct);
    }

    private async Task<Result<ImportValidationStartResultDto>> ResultAsync(ImportBatch batch, bool started, CancellationToken ct)
        => Result<ImportValidationStartResultDto>.Success(
            new ImportValidationStartResultDto(started, batch.ToDto(await _tableInfo.LoadAsync(batch, ct))));

    private static Result<ImportValidationStartResultDto> Fail(string code, string message)
        => Result<ImportValidationStartResultDto>.Failure(code, message);
}
