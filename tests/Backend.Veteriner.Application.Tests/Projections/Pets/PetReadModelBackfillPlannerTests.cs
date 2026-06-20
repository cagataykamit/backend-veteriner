using Backend.Veteriner.Application.Projections.Pets;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Projections.Pets;

public sealed class PetReadModelBackfillPlannerTests
{
    private static readonly DateTime Baseline = PetReadModelBackfillPlanner.BackfillBaselineOccurredAtUtc;
    private static readonly DateTime NewerEvent = new(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResolveOccurredAtUtc_Should_ReturnBaselineSentinel()
        => PetReadModelBackfillPlanner.ResolveOccurredAtUtc().Should().Be(Baseline);

    [Fact]
    public void Decide_Should_Insert_WhenNoExistingRow()
        => PetReadModelBackfillPlanner.Decide(Baseline, existingLastEventOccurredAtUtc: null)
            .Should().Be(PetReadModelBackfillAction.Insert);

    [Fact]
    public void Decide_Should_Update_WhenExistingRowHasBaselineTimestamp()
        => PetReadModelBackfillPlanner.Decide(Baseline, existingLastEventOccurredAtUtc: Baseline)
            .Should().Be(PetReadModelBackfillAction.Update);

    [Fact]
    public void Decide_Should_SkipStale_WhenExistingRowHasAnyRealEventTimestamp()
        => PetReadModelBackfillPlanner.Decide(Baseline, existingLastEventOccurredAtUtc: NewerEvent)
            .Should().Be(PetReadModelBackfillAction.SkipStale);
}
