using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicByIdSpec : Specification<Clinic>
{
    public ClinicByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(c => c.TenantId == tenantId && c.Id == id);
    }
}
