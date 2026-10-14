using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.Validate;

/// <summary>
/// Kuru çalıştırma (IMPORT_API_CONTRACT.md §7.4). Senkron ve kısa iştir: parti <c>Validating</c> durumuna geçirilip
/// kaydedilir (kiracıda tek aktif iş indeksi), tüm satırlar bellekte işlenir, satır sonuçları ve parti durumu tek
/// <c>SaveChanges</c>'te yazılır. Okuma sorguları satır sayısından bağımsızdır (katalog + mevcut veri toplu).
/// </summary>
public sealed class ValidateImportBatchCommandHandler : IRequestHandler<ValidateImportBatchCommand, Result<ImportBatchDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ImportBatch> _batches;
    private readonly IRepository<ImportBatchRow> _rows;
    private readonly ImportCatalogLoader _catalogs;
    private readonly ImportExistingDataLoader _existingData;
    private readonly ImportTableInfoLoader _tableInfo;
    private readonly TimeProvider _timeProvider;

    public ValidateImportBatchCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ImportBatch> batches,
        IRepository<ImportBatchRow> rows,
        ImportCatalogLoader catalogs,
        ImportExistingDataLoader existingData,
        ImportTableInfoLoader tableInfo,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _rows = rows;
        _catalogs = catalogs;
        _existingData = existingData;
        _tableInfo = tableInfo;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ImportBatchDto>> Handle(ValidateImportBatchCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id, tracking: true), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportBatchDto>.Failure(ImportBatchVisibility.NotFound());

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Kirası dolmamış doğrulama sürüyor: ikinci istek bekletilmez (kiracıda tek aktif iş).
        if (batch.Status == ImportBatchStatus.Validating && !batch.IsValidationLeaseExpired(now))
            return Fail("Imports.AnotherJobRunning", "Bu parti şu anda doğrulanıyor; bitmesini bekleyin.");

        if (!batch.CanStartValidation(now))
            return Fail("Imports.InvalidState", $"Parti '{batch.Status}' durumundayken doğrulanamaz; önce eşleme kaydedin.");

        var present = ImportTableInfoLoader.PresentTables(batch);
        var mapping = ImportMappingDocument.Parse(batch.MappingJson);
        var missing = ImportPreconditions.MissingRequired(mapping, present);
        if (missing.Count > 0)
            return Fail("Imports.MappingIncomplete", $"Eksik zorunlu alan eşlemesi — {string.Join("; ", missing)}.");

        var started = batch.StartValidation(now);
        if (!started.IsSuccess)
            return Result<ImportBatchDto>.Failure(started.Error);

        var saved = await SaveStartAsync(ct);
        if (saved.HasValue)
            return saved.Value;

        try
        {
            if (ImportPreconditions.Check(mapping, present).Count > 0)
                batch.FailValidation(now);
            else
                await ValidateRowsAsync(batch, tenantId, mapping, now, ct);

            // Satır sonuçları ve parti durumu tek işlemde kalıcılaşır.
            await _batches.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("Imports.ConcurrencyConflict", "Parti eşzamanlı olarak değişti; durumu yeniden okuyup tekrar deneyin.");
        }
        catch (Exception)
        {
            // Beklenmeyen hata: parti takılı kalmasın (kira süresi dolana kadar yeni doğrulama reddedilir).
            await TryFailAsync(batch);
            throw;
        }

        return Result<ImportBatchDto>.Success(batch.ToDto(await _tableInfo.LoadAsync(batch, ct)));
    }

    private async Task ValidateRowsAsync(ImportBatch batch, Guid tenantId, ImportMappingDocument mapping, DateTime now, CancellationToken ct)
    {
        var rows = await _rows.ListAsync(new ImportBatchRowsByBatchSpec(batch.Id), ct);
        var tables = rows
            .GroupBy(r => r.Table)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ImportSourceRow>)g
                    .OrderBy(r => r.RowNumber)
                    .Select(r => ImportSourceRow.FromJson(r.RowNumber, r.SourceJson))
                    .ToList());

        var catalogs = await _catalogs.LoadAsync(tenantId, ct);
        var existing = await _existingData.LoadAsync(tenantId, ImportLookupKeys.From(tables, mapping), ct);

        var output = ImportValidationEngine.Run(new ImportValidationInput(
            tables, mapping, ImportOptionsDocument.Parse(batch.OptionsJson), catalogs, existing, now));

        var byKey = output.Rows.ToDictionary(r => (r.Table, r.RowNumber));
        foreach (var row in rows)
        {
            var result = byKey[(row.Table, row.RowNumber)];
            row.ApplyValidation(
                result.Status,
                ImportIssueJson.Serialize(result.Issues),
                ImportNormalizedJson.Serialize(result.Normalized));
        }

        var customers = output.CountsOf(ImportTable.Customers);
        var pets = output.CountsOf(ImportTable.Pets);
        var vaccinations = output.CountsOf(ImportTable.Vaccinations);

        var importable = customers.Valid + customers.Warning + pets.Valid + pets.Warning + vaccinations.Valid + vaccinations.Warning;
        var completed = importable == 0
            ? batch.FailValidation(now, customers, pets, vaccinations)
            : batch.CompleteValidation(Guid.NewGuid(), customers, pets, vaccinations, now);
        if (!completed.IsSuccess)
            throw new InvalidOperationException(completed.Error.Message);
    }

    private async Task<Result<ImportBatchDto>?> SaveStartAsync(CancellationToken ct)
    {
        try
        {
            await _batches.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("Imports.ConcurrencyConflict", "Parti eşzamanlı olarak değişti; durumu yeniden okuyup tekrar deneyin.");
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains(ImportBatch.ActiveJobIndexName, StringComparison.Ordinal))
        {
            return Fail("Imports.AnotherJobRunning", "Kiracıda başka bir içe aktarma işi çalışıyor; bitmesini bekleyin.");
        }
    }

    private async Task TryFailAsync(ImportBatch batch)
    {
        try
        {
            if (batch.FailValidation(_timeProvider.GetUtcNow().UtcDateTime).IsSuccess)
                await _batches.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Asıl hata yeniden fırlatılır; kira süresi dolunca parti yeniden doğrulanabilir.
        }
    }

    private static Result<ImportBatchDto> Fail(string code, string message)
        => Result<ImportBatchDto>.Failure(code, message);
}
