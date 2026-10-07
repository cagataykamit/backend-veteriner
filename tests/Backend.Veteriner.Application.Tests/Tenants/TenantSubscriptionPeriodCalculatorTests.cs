using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Tenants;

public sealed class TenantSubscriptionPeriodCalculatorTests
{
    [Fact]
    public void ResolveCurrentWindow_Should_Not_RollForward_When_ActiveSubscription_PeriodAlreadyEnded()
    {
        var utcNow = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        var activatedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var sub = TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Pro, activatedAt.AddDays(-14), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, activatedAt);

        var window = TenantSubscriptionPeriodCalculator.ResolveCurrentWindow(sub, utcNow);

        window.PeriodStartUtc.Should().Be(activatedAt);
        window.PeriodEndUtc.Should().Be(activatedAt.AddMonths(1));
    }

    [Fact]
    public void ResolveCurrentWindow_Should_ReturnTrialWindow_Unchanged_When_Trialing()
    {
        var trialStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sub = TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Basic, trialStart, 14);
        var utcNow = trialStart.AddDays(50);

        var window = TenantSubscriptionPeriodCalculator.ResolveCurrentWindow(sub, utcNow);

        window.PeriodStartUtc.Should().Be(trialStart);
        window.PeriodEndUtc.Should().Be(sub.TrialEndsAtUtc);
        window.BillingCycleAnchorUtc.Should().Be(trialStart);
    }
}
