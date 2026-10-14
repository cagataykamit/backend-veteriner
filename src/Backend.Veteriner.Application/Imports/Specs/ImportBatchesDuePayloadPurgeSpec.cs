using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>
/// Satır verisinin saklama süresi (<see cref="ImportBatch.PayloadRetention"/>, K9) dolmuş ve henüz temizlenmemiş
/// partiler. <see cref="ImportBatch.PayloadPurgeDueAtUtc"/> ile aynı kural; temizlik işi B5'tedir.
/// </summary>
public sealed class ImportBatchesDuePayloadPurgeSpec : Specification<ImportBatch>
{
    public ImportBatchesDuePayloadPurgeSpec(DateTime nowUtc, int batchSize)
    {
        var retained = ImportBatchStatusSets.PayloadRetained.ToArray();
        var cutoff = nowUtc - ImportBatch.PayloadRetention;

        Query.Where(b => b.PayloadPurgedAtUtc == null
                         && retained.Contains(b.Status)
                         && (b.UpdatedAtUtc ?? b.CreatedAtUtc) <= cutoff);
        Query.OrderBy(b => b.UpdatedAtUtc).Take(batchSize);
    }
}
