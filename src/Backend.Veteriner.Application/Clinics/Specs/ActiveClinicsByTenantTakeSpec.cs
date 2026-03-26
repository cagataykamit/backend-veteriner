using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ActiveClinicsByTenantTakeSpec : Specification<Clinic>
{
    public ActiveClinicsByTenantTakeSpec(Guid tenantId, int take)
    {
        Query.Where(c => c.TenantId == tenantId && c.IsActive)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Take(Math.Max(1, take));
    }
}