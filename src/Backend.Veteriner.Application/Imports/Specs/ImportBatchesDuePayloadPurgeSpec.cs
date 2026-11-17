using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>
/// Geri alma süresi dolmuş, satırları henüz silinmemiş aktarılmış partiler (ADR-051 eki). <see cref="ImportBatch.PayloadPurgeDueAtUtc"/>
/// (= <see cref="ImportBatch.RollbackDeadlineUtc"/>) ile aynı kural; bakım işi kullanır. Salt okunur (işçi her partiyi kendi kapsamında
/// yeniden okur).
/// </summary>
public sealed class ImportBatchesDuePayloadPurgeSpec : Specification<ImportBatch>
{
    public ImportBatchesDuePayloadPurgeSpec(DateTime nowUtc, int batchSize)
    {
        var applied = ImportBatchStatusSets.RollbackableFrom.ToArray();
        var cutoff = nowUtc - ImportBatch.RollbackWindow;

        Query.AsNoTracking();
        Query.Where(b => b.PayloadPurgedAtUtc == null
                         && applied.Contains(b.Status)
                         && (b.CommittedAtUtc ?? b.ValidatedAtUtc ?? b.CreatedAtUtc) <= cutoff);
        Query.OrderBy(b => b.CommittedAtUtc).Take(batchSize);
    }
}

/// <summary>
/// Kaydıyla birlikte silinecek kapanmış partiler (ADR-051 eki): <see cref="ImportBatchStatusSets.Closed"/> ve son hareketten
/// <see cref="ImportBatch.RollbackWindow"/> geçmiş. <see cref="ImportBatch.DeletionDueAtUtc"/> ile aynı kural. Salt okunur.
/// </summary>
public sealed class ImportBatchesDueDeletionSpec : Specification<ImportBatch>
{
    public ImportBatchesDueDeletionSpec(DateTime nowUtc, int batchSize)
    {
        var closed = ImportBatchStatusSets.Closed.ToArray();
        var cutoff = nowUtc - ImportBatch.RollbackWindow;

        Query.AsNoTracking();
        Query.Where(b => closed.Contains(b.Status) && (b.UpdatedAtUtc ?? b.CreatedAtUtc) <= cutoff);
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
