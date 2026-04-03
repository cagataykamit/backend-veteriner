using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Specs;

public sealed class HospitalizationByIdSpec : Specification<Hospitalization>
{
    public HospitalizationByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
