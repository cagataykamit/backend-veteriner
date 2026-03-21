using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Dashboard.Specs;

public sealed class DashboardPetsTotalCountSpec : Specification<Pet>
{
    public DashboardPetsTotalCountSpec(Guid tenantId)
    {
        Query.Where(p => p.TenantId == tenantId);
    }
}
