using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>Partinin bir tablosunun ilk satırı (başlıkları taşır); salt okunur.</summary>
public sealed class ImportFirstRowSpec : Specification<ImportBatchRow>
{
    public ImportFirstRowSpec(Guid batchId, ImportTable table)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId && r.Table == table);
        Query.OrderBy(r => r.RowNumber);
        Query.Take(1);
    }
}

/// <summary>Partinin verilen satırları (izlenir; uygulama sonucu yazmak için). Kimlik kümesi tek JSON parametresiyle gider.</summary>
public sealed class ImportBatchRowsByIdsSpec : Specification<ImportBatchRow>
{
    public ImportBatchRowsByIdsSpec(Guid batchId, IReadOnlyCollection<Guid> rowIds, bool pendingOnly = false)
    {
        var ids = rowIds.ToArray();
        Query.Where(r => r.BatchId == batchId && ids.Contains(r.Id));
        if (pendingOnly)
            Query.Where(r => r.ApplyStatus == ImportRowApplyStatus.Pending);
    }
}

/// <summary>Uygulama sonucu yazılmış satır sayısı (ilerleme).</summary>
public sealed class ImportAppliedRowsSpec : Specification<ImportBatchRow>
{
    public ImportAppliedRowsSpec(Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId && r.ApplyStatus != ImportRowApplyStatus.Pending);
    }
}

/// <summary>Doğrulama sonucu yazılmış satır sayısı (doğrulama ilerlemesi; işçi tur başında sonuçları sıfırlar).</summary>
public sealed class ImportValidatedRowsSpec : Specification<ImportBatchRow>
{
    public ImportValidatedRowsSpec(Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId && r.ValidationStatus != ImportRowValidationStatus.Pending);
    }
}

/// <summary>Partinin tüm hazırlık satırları (izlenir; silme için).</summary>
public sealed class ImportBatchRowsByBatchSpec : Specification<ImportBatchRow>
{
    public ImportBatchRowsByBatchSpec(Guid batchId)
    {
        Query.Where(r => r.BatchId == batchId);
    }
}
