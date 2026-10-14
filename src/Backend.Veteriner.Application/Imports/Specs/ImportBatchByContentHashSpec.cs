using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>
/// Kiracıda aynı içerik özetini <b>tutan</b> parti (özeti serbest bırakan durumlar hariç) — veritabanındaki filtreli
/// benzersiz indeksle aynı küme (<see cref="ImportBatchStatusSets.ContentHashReleased"/>).
/// </summary>
public sealed class ImportBatchByContentHashSpec : Specification<ImportBatch>
{
    public ImportBatchByContentHashSpec(Guid tenantId, string contentHash)
    {
        var released = ImportBatchStatusSets.ContentHashReleased.ToArray();
        Query.AsNoTracking();
        Query.Where(b => b.TenantId == tenantId && b.ContentHash == contentHash && !released.Contains(b.Status));
    }
}
