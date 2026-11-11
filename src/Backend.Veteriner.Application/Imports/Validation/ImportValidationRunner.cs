using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports.Validation;

/// <summary>
/// Kuru çalıştırmanın kuralları (IMPORT_API_CONTRACT.md §7.4): dosya düzeyi önkoşullar, katalog + mevcut veri toplu okuması
/// ve <see cref="ImportValidationEngine"/>. Hiçbir şey yazmaz; satır sonuçlarını ve parti sonucunu döndürür. Arka plan
/// doğrulama işi (ADR-019 K13) sonuçları satırlara parça parça yazar ve partiyi <see cref="ImportValidationOutcome.Complete"/>
/// ile kapatır. Okuma sorguları satır sayısından bağımsızdır.
/// </summary>
public sealed class ImportValidationRunner
{
    private readonly ImportCatalogLoader _catalogs;
    private readonly ImportExistingDataLoader _existingData;

    public ImportValidationRunner(ImportCatalogLoader catalogs, ImportExistingDataLoader existingData)
    {
        _catalogs = catalogs;
        _existingData = existingData;
    }

    /// <param name="rows">Partinin tüm hazırlık satırları.</param>
    public async Task<ImportValidationOutcome> EvaluateAsync(
        ImportBatch batch, IReadOnlyList<ImportBatchRow> rows, DateTime nowUtc, CancellationToken ct)
    {
        var present = ImportTableInfoLoader.PresentTables(batch);
        var mapping = ImportMappingDocument.Parse(batch.MappingJson);

        // Dosya düzeyi sorun: satırlar işlenmez, önceki satır sonuçlarına dokunulmaz.
        if (ImportPreconditions.Check(mapping, present, batch.ClinicId is not null).Count > 0)
            return ImportValidationOutcome.FileLevelFailure;

        var tables = rows
            .GroupBy(r => r.Table)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ImportSourceRow>)g
                    .OrderBy(r => r.RowNumber)
                    .Select(r => ImportSourceRow.FromJson(r.RowNumber, r.SourceJson))
                    .ToList());

        var catalogs = await _catalogs.LoadAsync(batch.TenantId, ct);
        var existing = await _existingData.LoadAsync(
            batch.TenantId, batch.ClinicId, ImportLookupKeys.From(tables, mapping), present, nowUtc, ct);

        var output = ImportValidationEngine.Run(new ImportValidationInput(
            tables, mapping, ImportOptionsDocument.Parse(batch.OptionsJson), catalogs, existing, nowUtc));

        return new ImportValidationOutcome(
            output.Rows.ToDictionary(r => (r.Table, r.RowNumber)),
            ImportCanonicalNames.Tables.ToDictionary(t => t, output.CountsOf));
    }
}

/// <summary>Bir doğrulama turunun sonucu; satırlara yazma ve partiyi kapatma kuralı tek yerde.</summary>
public sealed class ImportValidationOutcome
{
    /// <summary>Dosya düzeyi başarısızlık (bağlantı sütunu yok): satır sonucu yok, parti <c>ValidationFailed</c>.</summary>
    public static readonly ImportValidationOutcome FileLevelFailure = new(null, null);

    private readonly IReadOnlyDictionary<(ImportTable Table, int RowNumber), ImportRowResult>? _rows;
    private readonly IReadOnlyDictionary<ImportTable, ImportTableCounts>? _counts;

    public ImportValidationOutcome(
        IReadOnlyDictionary<(ImportTable Table, int RowNumber), ImportRowResult>? rows,
        IReadOnlyDictionary<ImportTable, ImportTableCounts>? counts)
    {
        _rows = rows;
        _counts = counts;
    }

    /// <summary>Satır sonuçları var mı (dosya düzeyi başarısızlıkta yok).</summary>
    public bool HasRowResults => _rows is not null;

    /// <summary>Satıra kendi sonucunu yazar (önceki sonucun yerini alır).</summary>
    public void ApplyTo(ImportBatchRow row)
    {
        if (_rows is null)
            throw new InvalidOperationException("Dosya düzeyi başarısızlıkta satır sonucu yoktur.");

        var result = _rows[(row.Table, row.RowNumber)];
        row.ApplyValidation(
            result.Status,
            ImportIssueJson.Serialize(result.Issues),
            ImportNormalizedJson.Serialize(result.Normalized));
    }

    /// <summary>
    /// Partiyi kapatır: dosya düzeyi sorun veya hiç aktarılabilir satır yoksa <c>ValidationFailed</c> (sayaçlar korunur),
    /// aksi <c>Validated</c> + yeni <c>validationId</c>.
    /// </summary>
    public Result Complete(ImportBatch batch, DateTime nowUtc)
    {
        if (_counts is null)
            return batch.FailValidation(nowUtc, counts: null);

        var importable = _counts.Values.Sum(c => c.Valid + c.Warning);
        return importable == 0
            ? batch.FailValidation(nowUtc, _counts)
            : batch.CompleteValidation(Guid.NewGuid(), _counts, nowUtc);
    }
}
