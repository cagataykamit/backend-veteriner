using Backend.Veteriner.Application.Common.Time;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common.Time;

public sealed class OperationDayBoundsTests
{
    [Fact]
    public void ForUtcNow_Should_Return_Istanbul_Local_Day_Bounds_AsUtc()
    {
        var utcNow = new DateTime(2026, 03, 22, 00, 30, 00, DateTimeKind.Utc);

        var (startUtc, endUtc) = OperationDayBounds.ForUtcNow(utcNow);

        startUtc.Should().Be(new DateTime(2026, 03, 21, 21, 00, 00, DateTimeKind.Utc));
        endUtc.Should().Be(new DateTime(2026, 03, 22, 21, 00, 00, DateTimeKind.Utc));
    }
}
