using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicsByTenantCountSpec : Specification<Clinic>
{
    public ClinicsByTenantCountSpec(Guid tenantId)
    {
        Query.Where(c => c.TenantId == tenantId);
    }
}
