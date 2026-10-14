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
        if (ImportPreconditions.Check(mapping, present).Count > 0)
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
        var existing = await _existingData.LoadAsync(batch.TenantId, ImportLookupKeys.From(tables, mapping), ct);

        var output = ImportValidationEngine.Run(new ImportValidationInput(
            tables, mapping, ImportOptionsDocument.Parse(batch.OptionsJson), catalogs, existing, nowUtc));

        return new ImportValidationOutcome(
            output.Rows.ToDictionary(r => (r.Table, r.RowNumber)),
            output.CountsOf(ImportTable.Customers),
            output.CountsOf(ImportTable.Pets),
            output.CountsOf(ImportTable.Vaccinations));
    }
}

/// <summary>Bir doğrulama turunun sonucu; satırlara yazma ve partiyi kapatma kuralı tek yerde.</summary>
public sealed class ImportValidationOutcome
{
    /// <summary>Dosya düzeyi başarısızlık (bağlantı sütunu yok): satır sonucu yok, parti <c>ValidationFailed</c>.</summary>
    public static readonly ImportValidationOutcome FileLevelFailure = new(null, null, null, null);

    private readonly IReadOnlyDictionary<(ImportTable Table, int RowNumber), ImportRowResult>? _rows;
    private readonly ImportTableCounts? _customers;
    private readonly ImportTableCounts? _pets;
    private readonly ImportTableCounts? _vaccinations;

    public ImportValidationOutcome(
        IReadOnlyDictionary<(ImportTable Table, int RowNumber), ImportRowResult>? rows,
        ImportTableCounts? customers,
        ImportTableCounts? pets,
        ImportTableCounts? vaccinations)
    {
        _rows = rows;
        _customers = customers;
        _pets = pets;
        _vaccinations = vaccinations;
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
        if (_customers is not { } customers || _pets is not { } pets || _vaccinations is not { } vaccinations)
            return batch.FailValidation(nowUtc);

        var importable = customers.Valid + customers.Warning + pets.Valid + pets.Warning + vaccinations.Valid + vaccinations.Warning;
        return importable == 0
            ? batch.FailValidation(nowUtc, customers, pets, vaccinations)
            : batch.CompleteValidation(Guid.NewGuid(), customers, pets, vaccinations, nowUtc);
    }
}
