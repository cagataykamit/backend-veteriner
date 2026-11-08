using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ActiveClinicsByTenantCountSpec : Specification<Clinic>
{
    public ActiveClinicsByTenantCountSpec(Guid tenantId)
    {
        Query.Where(c => c.TenantId == tenantId && c.IsActive);
    }
}
