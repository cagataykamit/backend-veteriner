using Backend.Veteriner.Application.Projections.Clients;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Projections.Clients;

public sealed class ClientReadModelBackfillPlannerTests
{
    private static readonly DateTime Created = new(2026, 6, 19, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Updated = new(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResolveOccurredAtUtc_Should_PreferUpdatedAtUtc_WhenPresent()
        => ClientReadModelBackfillPlanner.ResolveOccurredAtUtc(Created, Updated).Should().Be(Updated);

    [Fact]
    public void ResolveOccurredAtUtc_Should_FallBackToCreatedAtUtc_WhenNotUpdated()
        => ClientReadModelBackfillPlanner.ResolveOccurredAtUtc(Created, null).Should().Be(Created);

    [Fact]
    public void Decide_Should_Insert_WhenNoExistingRow()
        => ClientReadModelBackfillPlanner.Decide(Created, existingLastEventOccurredAtUtc: null)
            .Should().Be(ClientReadModelBackfillAction.Insert);

    [Fact]
    public void Decide_Should_Update_WhenBackfillIsNewerThanExisting()
        => ClientReadModelBackfillPlanner.Decide(Updated, existingLastEventOccurredAtUtc: Created)
            .Should().Be(ClientReadModelBackfillAction.Update);

    [Fact]
    public void Decide_Should_Update_WhenEqual_SoReRunIsIdempotent()
        => ClientReadModelBackfillPlanner.Decide(Created, existingLastEventOccurredAtUtc: Created)
            .Should().Be(ClientReadModelBackfillAction.Update);

    [Fact]
    public void Decide_Should_SkipStale_WhenExistingRowIsNewer()
        => ClientReadModelBackfillPlanner.Decide(Created, existingLastEventOccurredAtUtc: Updated)
            .Should().Be(ClientReadModelBackfillAction.SkipStale);
}
