using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Tenants;

public sealed class TenantSubscriptionEffectiveWriteEvaluatorTests
{
    [Fact]
    public void GetEffectiveStatus_Should_ReturnReadOnly_When_ActiveSubscription_ActivatedOverAMonthAgo()
    {
        var utcNow = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        var activatedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var sub = TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Pro, activatedAt.AddDays(-14), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, activatedAt);

        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, utcNow);

        effective.Should().Be(TenantSubscriptionStatus.ReadOnly);
    }

    [Fact]
    public void GetEffectiveStatus_Should_ReturnActive_When_ActiveSubscription_WithinCurrentPaidPeriod()
    {
        var utcNow = DateTime.UtcNow;
        var activatedAt = utcNow.AddDays(-10);
        var sub = TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Pro, activatedAt.AddDays(-14), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, activatedAt);

        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, utcNow);

        effective.Should().Be(TenantSubscriptionStatus.Active);
    }

    [Fact]
    public void GetEffectiveStatus_Should_ReturnReadOnly_When_TrialExpired()
    {
        var utcNow = DateTime.UtcNow;
        var sub = TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Basic, utcNow.AddDays(-20), 14);

        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, utcNow);

        effective.Should().Be(TenantSubscriptionStatus.ReadOnly);
    }

    [Fact]
    public void GetEffectiveStatus_Should_ReturnTrialing_When_TrialNotExpired()
    {
        var utcNow = DateTime.UtcNow;
        var sub = TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Basic, utcNow.AddDays(-1), 14);

        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, utcNow);

        effective.Should().Be(TenantSubscriptionStatus.Trialing);
    }

    [Fact]
    public void GetEffectiveStatus_Should_ReturnCancelled_When_SubscriptionCancelled()
    {
        var utcNow = DateTime.UtcNow;
        var activatedAt = utcNow.AddDays(-90);
        var sub = TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Pro, activatedAt.AddDays(-14), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, activatedAt);
        typeof(TenantSubscription).GetProperty(nameof(TenantSubscription.Status))!
            .SetValue(sub, TenantSubscriptionStatus.Cancelled);

        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, utcNow);

        effective.Should().Be(TenantSubscriptionStatus.Cancelled);
    }
}
