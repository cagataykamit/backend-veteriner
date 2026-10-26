using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Tenants;

public sealed class TenantSubscriptionBillingPeriodTests
{
    private static readonly DateTime TrialStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TenantSubscription Trial()
        => TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Basic, TrialStart, 14);

    [Fact]
    public void ActivatePaidPlan_Should_Start_Yearly_Period_Of_One_Year()
    {
        var sub = Trial();
        var paidAt = TrialStart.AddDays(14);

        sub.ActivatePaidPlan(SubscriptionPlanCode.Basic, BillingPeriodKind.Yearly, paidAt);

        sub.BillingPeriod.Should().Be(BillingPeriodKind.Yearly);
        sub.ActivatedAtUtc.Should().Be(paidAt);
        sub.PaidPeriodEndUtc.Should().Be(paidAt.AddYears(1));
    }

    [Fact]
    public void ActivatePaidPlan_Should_Default_To_Monthly_Period()
    {
        var sub = Trial();
        var paidAt = TrialStart.AddDays(14);

        sub.ActivatePaidPlan(SubscriptionPlanCode.Basic, paidAt);

        sub.BillingPeriod.Should().Be(BillingPeriodKind.Monthly);
        sub.PaidPeriodEndUtc.Should().Be(paidAt.AddMonths(1));
    }

    [Fact]
    public void ActivatePaidPlan_Should_Restart_Period_When_Switching_Monthly_To_Yearly_Mid_Period()
    {
        var sub = Trial();
        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, TrialStart.AddDays(14));
        var switchAt = TrialStart.AddDays(30);

        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, BillingPeriodKind.Yearly, switchAt);

        sub.BillingPeriod.Should().Be(BillingPeriodKind.Yearly);
        sub.ActivatedAtUtc.Should().Be(switchAt);
        sub.PaidPeriodEndUtc.Should().Be(switchAt.AddYears(1));
    }

    [Fact]
    public void ActivatePaidPlan_Should_Keep_Yearly_Anchor_When_Upgrading_Mid_Period()
    {
        var sub = Trial();
        var paidAt = TrialStart.AddDays(14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Basic, BillingPeriodKind.Yearly, paidAt);

        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, paidAt.AddMonths(5));

        sub.PlanCode.Should().Be(SubscriptionPlanCode.Pro);
        sub.BillingPeriod.Should().Be(BillingPeriodKind.Yearly);
        sub.ActivatedAtUtc.Should().Be(paidAt);
    }

    [Fact]
    public void ActivatePaidPlan_Should_Start_New_Monthly_Period_When_Yearly_Expired_And_Monthly_Chosen()
    {
        var sub = Trial();
        var paidAt = TrialStart.AddDays(14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Basic, BillingPeriodKind.Yearly, paidAt);
        var renewAt = paidAt.AddYears(1).AddDays(3);

        sub.ActivatePaidPlan(SubscriptionPlanCode.Basic, BillingPeriodKind.Monthly, renewAt);

        sub.BillingPeriod.Should().Be(BillingPeriodKind.Monthly);
        sub.ActivatedAtUtc.Should().Be(renewAt);
        sub.PaidPeriodEndUtc.Should().Be(renewAt.AddMonths(1));
    }

    [Fact]
    public void BillingCheckoutSession_Should_Carry_Requested_Period()
    {
        var now = TrialStart;
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(), SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, BillingProvider.Manual, now, now.AddMinutes(30), BillingPeriodKind.Yearly);

        session.BillingPeriod.Should().Be(BillingPeriodKind.Yearly);
    }

    [Fact]
    public void BillingCheckoutSession_Should_Reject_Undefined_Period()
    {
        var now = TrialStart;
        var act = () => BillingCheckoutSession.CreatePending(
            Guid.NewGuid(), SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, BillingProvider.Manual, now, now.AddMinutes(30), (BillingPeriodKind)9);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PeriodPriceMinor_Should_Price_Yearly_As_Ten_Months()
    {
        SubscriptionPricing.PeriodPriceMinor(99_900, BillingPeriodKind.Yearly).Should().Be(999_000);
        SubscriptionPricing.PeriodPriceMinor(99_900, BillingPeriodKind.Monthly).Should().Be(99_900);
    }
}
