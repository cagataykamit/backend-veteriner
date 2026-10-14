using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>
/// Satır verisinin saklama süresi (<see cref="ImportBatch.PayloadRetention"/>, K9) dolmuş ve henüz temizlenmemiş
/// partiler. <see cref="ImportBatch.PayloadPurgeDueAtUtc"/> ile aynı kural; B5 bakım işi kullanır. Salt okunur
/// (işçi her partiyi kendi kapsamında yeniden okur).
/// </summary>
public sealed class ImportBatchesDuePayloadPurgeSpec : Specification<ImportBatch>
{
    public ImportBatchesDuePayloadPurgeSpec(DateTime nowUtc, int batchSize)
    {
        var retained = ImportBatchStatusSets.PayloadRetained.ToArray();
        var cutoff = nowUtc - ImportBatch.PayloadRetention;

        Query.AsNoTracking();
        Query.Where(b => b.PayloadPurgedAtUtc == null
                         && retained.Contains(b.Status)
                         && (b.UpdatedAtUtc ?? b.CreatedAtUtc) <= cutoff);
        Query.OrderBy(b => b.UpdatedAtUtc).Take(batchSize);
    }
}

/// <summary>
/// Süresi dolan taslaklar: arka plan işi sürmeyen taslak ve son hareketten (<c>UpdatedAtUtc</c>, yoksa oluşturma)
/// <see cref="ImportBatch.DraftLifetime"/> geçmiş. <see cref="ImportBatch.Expire"/> ile aynı kural. Salt okunur.
/// </summary>
public sealed class ImportBatchesDueExpirySpec : Specification<ImportBatch>
{
    public ImportBatchesDueExpirySpec(DateTime nowUtc, int batchSize)
    {
        var idle = ImportBatchStatusSets.IdleDraft.ToArray();
        var cutoff = nowUtc - ImportBatch.DraftLifetime;

        Query.AsNoTracking();
        Query.Where(b => idle.Contains(b.Status) && (b.UpdatedAtUtc ?? b.CreatedAtUtc) <= cutoff);
        Query.OrderBy(b => b.UpdatedAtUtc).Take(batchSize);
    }
}
