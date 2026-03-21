using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Dashboard.Specs;

public sealed class DashboardClientsTotalCountSpec : Specification<Client>
{
    public DashboardClientsTotalCountSpec(Guid tenantId)
    {
        Query.Where(c => c.TenantId == tenantId);
    }
}
