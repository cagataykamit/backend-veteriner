using Backend.Veteriner.Application.Projections.Appointments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Projections.Appointments;

public sealed class AppointmentProjectionPollingLoopTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public void ShouldIdleWaitAfterBatch_ReturnsFalse_WhenEventsProcessed(int processedCount)
    {
        AppointmentProjectionPollingLoop.ShouldIdleWaitAfterBatch(processedCount).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldIdleWaitAfterBatch_ReturnsTrue_WhenNoEventsProcessed(int processedCount)
    {
        AppointmentProjectionPollingLoop.ShouldIdleWaitAfterBatch(processedCount).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(-5, 1)]
    public void ResolveIdleInterval_EnforcesMinimumOneSecond(int configuredSeconds, int expectedSeconds)
    {
        AppointmentProjectionPollingLoop.ResolveIdleInterval(configuredSeconds)
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void ResolveIdleDelay_ReturnsZero_WhenBatchProcessedEvents()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

        AppointmentProjectionPollingLoop.ResolveIdleDelay(3, now.AddMinutes(-1), now, 2)
            .Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ResolveIdleDelay_UsesShortPoll_WithinActiveFollowUpWindow()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var lastActivity = now.AddSeconds(-2);

        AppointmentProjectionPollingLoop.ResolveIdleDelay(0, lastActivity, now, 2, 5, 100)
            .Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void ResolveIdleDelay_UsesFullInterval_AfterActiveFollowUpWindow()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var lastActivity = now.AddSeconds(-10);

        AppointmentProjectionPollingLoop.ResolveIdleDelay(0, lastActivity, now, 2, 5, 100)
            .Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ResolveIdleDelay_UsesFullInterval_WhenNoRecentActivity()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

        AppointmentProjectionPollingLoop.ResolveIdleDelay(0, null, now, 2, 5, 100)
            .Should().Be(TimeSpan.FromSeconds(2));
    }
}
