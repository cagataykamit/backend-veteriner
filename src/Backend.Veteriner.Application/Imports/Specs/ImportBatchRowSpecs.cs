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

/// <summary>Partinin tüm hazırlık satırları (izlenir; silme için).</summary>
public sealed class ImportBatchRowsByBatchSpec : Specification<ImportBatchRow>
{
    public ImportBatchRowsByBatchSpec(Guid batchId)
    {
        Query.Where(r => r.BatchId == batchId);
    }
}
