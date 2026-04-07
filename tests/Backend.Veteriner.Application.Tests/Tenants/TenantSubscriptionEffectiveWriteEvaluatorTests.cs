using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Tenants;

public sealed class TenantSubscriptionEffectiveWriteEvaluatorTests
{
    private static readonly DateTime Utc = new(2026, 4, 7, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TenantSubscriptionStatus.Active, true)]
    [InlineData(TenantSubscriptionStatus.ReadOnly, false)]
    [InlineData(TenantSubscriptionStatus.Cancelled, false)]
    public void Status_only_rules(TenantSubscriptionStatus status, bool expected)
    {
        TenantSubscriptionEffectiveWriteEvaluator
            .AllowsTenantMutations(status, trialEndsAtUtc: null, Utc)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void Trialing_without_end_allows()
    {
        TenantSubscriptionEffectiveWriteEvaluator
            .AllowsTenantMutations(TenantSubscriptionStatus.Trialing, null, Utc)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Trialing_before_end_allows()
    {
        TenantSubscriptionEffectiveWriteEvaluator
            .AllowsTenantMutations(TenantSubscriptionStatus.Trialing, Utc.AddDays(1), Utc)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Trialing_at_or_after_end_blocks_even_if_status_not_yet_updated()
    {
        TenantSubscriptionEffectiveWriteEvaluator
            .AllowsTenantMutations(TenantSubscriptionStatus.Trialing, Utc, Utc)
            .Should()
            .BeFalse();

        TenantSubscriptionEffectiveWriteEvaluator
            .AllowsTenantMutations(TenantSubscriptionStatus.Trialing, Utc.AddDays(-1), Utc)
            .Should()
            .BeFalse();
    }
}
