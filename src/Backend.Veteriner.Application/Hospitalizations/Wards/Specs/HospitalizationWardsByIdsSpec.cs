using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Specs;

public sealed class HospitalizationWardsByIdsSpec : Specification<HospitalizationWard>
{
    public HospitalizationWardsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.Where(x => x.TenantId == tenantId && ids.Contains(x.Id));
    }
}
