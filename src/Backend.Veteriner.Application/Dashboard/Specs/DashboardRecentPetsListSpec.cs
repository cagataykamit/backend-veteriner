using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary><see cref="Pet"/> için <see cref="DashboardRecentClientsListSpec"/> ile aynı sınırlama.</summary>
public sealed class DashboardRecentPetsListSpec : Specification<Pet>
{
    public DashboardRecentPetsListSpec(Guid tenantId, int take)
    {
        Query.Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.Id)
            .Take(take);
    }
}
