using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>Kiracıya ait parti; <paramref name="tracking"/> durum geçişi yapan komutlar içindir.</summary>
public sealed class ImportBatchByIdSpec : Specification<ImportBatch>
{
    public ImportBatchByIdSpec(Guid tenantId, Guid id, bool tracking = false)
    {
        if (!tracking)
            Query.AsNoTracking();
        Query.Where(b => b.TenantId == tenantId && b.Id == id);
    }
}
