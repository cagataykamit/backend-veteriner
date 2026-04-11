using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class DueScheduledPlanChangesSpec : Specification<ScheduledSubscriptionPlanChange>
{
    public DueScheduledPlanChangesSpec(DateTime utcNow, int batchSize)
    {
        Query.Where(x => x.Status == SubscriptionPlanChangeStatus.Scheduled && x.EffectiveAtUtc <= utcNow)
            .OrderBy(x => x.EffectiveAtUtc)
            .Take(batchSize);
    }
}
