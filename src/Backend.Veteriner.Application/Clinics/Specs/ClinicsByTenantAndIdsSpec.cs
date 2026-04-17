using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicsByTenantAndIdsSpec : Specification<Clinic>
{
    public ClinicsByTenantAndIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> clinicIds)
    {
        if (clinicIds.Count == 0)
            Query.Where(c => false);
        else
            Query.Where(c => c.TenantId == tenantId && clinicIds.Contains(c.Id));
    }
}
