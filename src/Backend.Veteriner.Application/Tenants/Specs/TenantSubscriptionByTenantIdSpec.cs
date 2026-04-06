using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantSubscriptionByTenantIdSpec : Specification<TenantSubscription>
{
    public TenantSubscriptionByTenantIdSpec(Guid tenantId)
    {
        Query.Where(s => s.TenantId == tenantId);
    }
}
