using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

public sealed class ImportBatchByRequestIdSpec : Specification<ImportBatch>
{
    public ImportBatchByRequestIdSpec(Guid tenantId, Guid requestId)
    {
        Query.AsNoTracking();
        Query.Where(b => b.TenantId == tenantId && b.RequestId == requestId);
    }
}
