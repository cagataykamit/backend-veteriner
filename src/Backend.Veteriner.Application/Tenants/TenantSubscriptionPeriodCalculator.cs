using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

public sealed record TenantSubscriptionPeriodWindow(
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTime BillingCycleAnchorUtc);

public static class TenantSubscriptionPeriodCalculator
{
    public static TenantSubscriptionPeriodWindow ResolveCurrentWindow(TenantSubscription sub, DateTime utcNow)
    {
        var now = EnsureUtc(utcNow);

        if (sub.Status == TenantSubscriptionStatus.Trialing && sub.TrialStartsAtUtc is { } trialStart && sub.TrialEndsAtUtc is { } trialEnd)
        {
            return new TenantSubscriptionPeriodWindow(EnsureUtc(trialStart), EnsureUtc(trialEnd), EnsureUtc(trialStart));
        }

        var anchor = EnsureUtc(sub.ActivatedAtUtc ?? sub.TrialStartsAtUtc ?? now);
        var periodStart = anchor;
        var periodEnd = periodStart.AddMonths(1);

        while (periodEnd <= now)
        {
            periodStart = periodEnd;
            periodEnd = periodStart.AddMonths(1);
        }

        return new TenantSubscriptionPeriodWindow(periodStart, periodEnd, anchor);
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
