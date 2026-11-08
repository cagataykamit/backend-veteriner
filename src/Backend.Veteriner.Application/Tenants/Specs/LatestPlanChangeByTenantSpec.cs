using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>Kiracının en son planladığı değişiklik (durumdan bağımsız).</summary>
public sealed class LatestPlanChangeByTenantSpec : Specification<ScheduledSubscriptionPlanChange>
{
    public LatestPlanChangeByTenantSpec(Guid tenantId)
    {
        Query.Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Take(1);
    }
}
