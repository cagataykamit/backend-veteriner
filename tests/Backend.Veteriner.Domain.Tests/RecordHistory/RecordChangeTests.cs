using Backend.Veteriner.Domain.RecordHistory;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.RecordHistory;

public sealed class RecordChangeTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private static RecordChange Create(RecordChangeKind kind, IReadOnlyList<RecordFieldChange> changes)
        => new(Guid.NewGuid(), Guid.NewGuid(), RecordHistoryTypes.Treatment, Guid.NewGuid(), kind, Guid.NewGuid(), Now, changes);

    [Fact]
    public void Update_without_field_changes_is_rejected()
    {
        var act = () => Create(RecordChangeKind.Updated, []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_keeps_field_changes()
    {
        var change = Create(RecordChangeKind.Updated, [new RecordFieldChange("Title", "Eski", "Yeni")]);

        change.Changes.Should().ContainSingle().Which.Should().Be(new RecordFieldChange("Title", "Eski", "Yeni"));
    }

    [Theory]
    [InlineData(RecordChangeKind.Created)]
    [InlineData(RecordChangeKind.Deleted)]
    public void Create_and_delete_carry_no_field_changes(RecordChangeKind kind)
    {
        var change = Create(kind, [new RecordFieldChange("Title", null, "Yeni")]);

        change.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Missing_actor_is_rejected()
    {
        var act = () => new RecordChange(
            Guid.NewGuid(), Guid.NewGuid(), RecordHistoryTypes.Treatment, Guid.NewGuid(), RecordChangeKind.Created, Guid.Empty, Now, []);

        act.Should().Throw<ArgumentException>();
    }
}
