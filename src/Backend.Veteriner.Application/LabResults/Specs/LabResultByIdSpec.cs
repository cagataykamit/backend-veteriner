using Ardalis.Specification;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Specs;

public sealed class LabResultByIdSpec : Specification<LabResult>
{
    public LabResultByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
