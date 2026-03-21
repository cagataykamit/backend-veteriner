using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicsByTenantPagedSpec : Specification<Clinic>
{
    public ClinicsByTenantPagedSpec(Guid tenantId, int page, int pageSize)
    {
        Query.Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
