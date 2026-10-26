using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;

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
    public void ResolveCurrentWindow_Should_Span_One_Year_When_Yearly()
    {
        var activatedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var sub = TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Pro, activatedAt.AddDays(-14), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, BillingPeriodKind.Yearly, activatedAt);

        var window = TenantSubscriptionPeriodCalculator.ResolveCurrentWindow(sub, activatedAt.AddMonths(4));

        window.PeriodStartUtc.Should().Be(activatedAt);
        window.PeriodEndUtc.Should().Be(activatedAt.AddYears(1));
    }

    [Fact]
    public void CalculatePeriodSwitch_Should_Credit_Unused_Part_Of_Current_Period()
    {
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(30);

        var result = SubscriptionProrationCalculator.CalculatePeriodSwitch(start.AddDays(15), start, end, 30_000, 300_000);

        result.ProrationRatio.Should().Be(0.5m);
        result.ProratedChargeMinor.Should().Be(285_000, "yıllık 300.000 - kullanılmayan yarım ay 15.000");
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
