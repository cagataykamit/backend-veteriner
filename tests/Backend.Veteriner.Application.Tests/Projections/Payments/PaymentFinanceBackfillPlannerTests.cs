using Backend.Veteriner.Application.Projections.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Projections.Payments;

public sealed class PaymentFinanceBackfillPlannerTests
{
    [Fact]
    public void Decide_Should_Insert_WhenNoExistingContribution()
    {
        var action = PaymentFinanceBackfillPlanner.Decide(
            PaymentFinanceBackfillPlanner.ResolveOccurredAtUtc(),
            existingLastEventOccurredAtUtc: null);

        action.Should().Be(PaymentFinanceBackfillAction.Insert);
    }

    [Fact]
    public void Decide_Should_Update_WhenExistingContributionIsOlderThanBackfillBaseline()
    {
        var action = PaymentFinanceBackfillPlanner.Decide(
            PaymentFinanceBackfillPlanner.ResolveOccurredAtUtc(),
            existingLastEventOccurredAtUtc: PaymentFinanceBackfillPlanner.BackfillBaselineOccurredAtUtc);

        action.Should().Be(PaymentFinanceBackfillAction.Update);
    }

    [Fact]
    public void Decide_Should_SkipStale_WhenExistingContributionIsNewerThanBackfillBaseline()
    {
        var action = PaymentFinanceBackfillPlanner.Decide(
            PaymentFinanceBackfillPlanner.ResolveOccurredAtUtc(),
            existingLastEventOccurredAtUtc: DateTime.UtcNow);

        action.Should().Be(PaymentFinanceBackfillAction.SkipStale);
    }
}
