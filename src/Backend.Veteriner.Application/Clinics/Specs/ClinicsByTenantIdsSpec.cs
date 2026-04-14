using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicsByTenantIdsSpec : Specification<Clinic>
{
    public ClinicsByTenantIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && clinicIds.Contains(c.Id));
    }
}
