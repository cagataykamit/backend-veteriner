using Backend.Veteriner.Application.Examinations;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Examinations;

public sealed class ExaminationExaminedAtUtcFilterTests
{
    [Fact]
    public void Resolve_Should_Map_Istanbul_Local_Date_To_HalfOpen_Utc_Range()
    {
        var localDate = new DateOnly(2026, 10, 8);

        var result = ExaminationExaminedAtUtcFilter.Resolve(localDate, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FromUtcInclusive.Should().Be(new DateTime(2026, 10, 7, 21, 0, 0, DateTimeKind.Utc));
        result.Value.ToUtcExclusive.Should().Be(new DateTime(2026, 10, 8, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_Should_Reject_Mixed_LocalDate_And_UtcBounds()
    {
        var result = ExaminationExaminedAtUtcFilter.Resolve(
            new DateOnly(2026, 10, 8),
            DateTime.UtcNow,
            null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.DateFilterInvalid");
    }
}
