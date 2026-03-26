using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicsByTenantFilteredSpec : Specification<Clinic>
{
    public ClinicsByTenantFilteredSpec(Guid tenantId, bool? isActive)
    {
        Query.Where(c => c.TenantId == tenantId);
        if (isActive.HasValue)
            Query.Where(c => c.IsActive == isActive.Value);

        Query.OrderBy(c => c.Name).ThenBy(c => c.Id);
    }
}

