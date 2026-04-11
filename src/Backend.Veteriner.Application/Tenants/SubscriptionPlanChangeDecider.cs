using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

public enum SubscriptionPlanChangeDecision
{
    Same = 0,
    Upgrade = 1,
    Downgrade = 2,
}

public static class SubscriptionPlanChangeDecider
{
    public static SubscriptionPlanChangeDecision Decide(SubscriptionPlanCode current, SubscriptionPlanCode target)
    {
        if (current == target)
            return SubscriptionPlanChangeDecision.Same;

        var currentRank = Rank(current);
        var targetRank = Rank(target);
        return targetRank > currentRank ? SubscriptionPlanChangeDecision.Upgrade : SubscriptionPlanChangeDecision.Downgrade;
    }

    private static int Rank(SubscriptionPlanCode code)
        => SubscriptionPlanCatalog.All
            .Select((p, index) => new { p.Code, index })
            .First(x => x.Code == code)
            .index;
}
