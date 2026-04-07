using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class UserTenantsByTenantCountSpec : Specification<UserTenant>
{
    public UserTenantsByTenantCountSpec(Guid tenantId)
    {
        Query.Where(x => x.TenantId == tenantId);
    }
}
