using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>Önizleme toplulaştırması için: sorunu olan satırların <c>IssuesJson</c> değerleri (yalnız tek sütun).</summary>
public sealed class ImportIssuesJsonSpec : Specification<ImportBatchRow, string?>
{
    public ImportIssuesJsonSpec(Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId && r.IssuesJson != null);
        Query.Select(r => r.IssuesJson);
    }
}

/// <summary>Hatırlatma etkisi için: hatasız aşı satırlarının <c>NormalizedJson</c> değerleri (yalnız tek sütun).</summary>
public sealed class ImportVaccinationNormalizedJsonSpec : Specification<ImportBatchRow, string?>
{
    public ImportVaccinationNormalizedJsonSpec(Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId
                         && r.Table == ImportTable.Vaccinations
                         && r.ValidationStatus != ImportRowValidationStatus.Error
                         && r.NormalizedJson != null);
        Query.Select(r => r.NormalizedJson);
    }
}

/// <summary>
/// Satır listesi süzgeci. <paramref name="validationStatus"/>/<paramref name="applyStatus"/> en çok biri dolu olur;
/// <paramref name="issueCodeFragment"/> <see cref="Validation.ImportIssueJson.CodeFragment"/> çıktısıdır.
/// <paramref name="page"/> ve <paramref name="pageSize"/> verilmezse sayfalama yoktur (toplam sayımı için).
/// </summary>
public sealed class ImportBatchRowsFilteredSpec : Specification<ImportBatchRow>
{
    public ImportBatchRowsFilteredSpec(
        Guid batchId,
        ImportTable? table,
        ImportRowValidationStatus? validationStatus,
        ImportRowApplyStatus? applyStatus,
        string? issueCodeFragment,
        int? page = null,
        int? pageSize = null)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId);

        if (table is { } t)
            Query.Where(r => r.Table == t);
        if (validationStatus is { } v)
            Query.Where(r => r.ValidationStatus == v);
        if (applyStatus is { } a)
            Query.Where(r => r.ApplyStatus == a);
        if (issueCodeFragment is not null)
            Query.Where(r => r.IssuesJson != null && r.IssuesJson.Contains(issueCodeFragment));

        if (page is { } p && pageSize is { } size)
        {
            Query.OrderBy(r => r.Table).ThenBy(r => r.RowNumber)
                .Skip((p - 1) * size)
                .Take(size);
        }
    }
}

/// <summary>Hata raporu satırları: doğrulaması hatalı veya uygulaması başarısız satırlar, tablo ve satır sırasıyla.</summary>
public sealed class ImportErrorRowsSpec : Specification<ImportBatchRow>
{
    public ImportErrorRowsSpec(Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId
                         && (r.ValidationStatus == ImportRowValidationStatus.Error
                             || r.ApplyStatus == ImportRowApplyStatus.Failed));
        Query.OrderBy(r => r.Table).ThenBy(r => r.RowNumber);
    }
}
