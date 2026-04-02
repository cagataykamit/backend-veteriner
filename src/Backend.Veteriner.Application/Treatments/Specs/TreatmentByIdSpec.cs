using Ardalis.Specification;
using Backend.Veteriner.Domain.Treatments;

namespace Backend.Veteriner.Application.Treatments.Specs;

public sealed class TreatmentByIdSpec : Specification<Treatment>
{
    public TreatmentByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(t => t.TenantId == tenantId && t.Id == id);
    }
}
