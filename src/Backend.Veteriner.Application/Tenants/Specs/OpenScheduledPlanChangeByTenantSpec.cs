using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class OpenScheduledPlanChangeByTenantSpec : Specification<ScheduledSubscriptionPlanChange>
{
    public OpenScheduledPlanChangeByTenantSpec(Guid tenantId)
    {
        Query.Where(x => x.TenantId == tenantId && x.Status == SubscriptionPlanChangeStatus.Scheduled)
            .OrderByDescending(x => x.RequestedAtUtc);
    }
}
