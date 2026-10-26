using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Outbox;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Common.Outbox;

public sealed class OutboxHealthEvaluatorTests
{
    private static readonly OutboxHealthOptions Defaults = new();

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenQueueEmpty()
    {
        var result = OutboxHealthEvaluator.Evaluate(new OutboxStatus(0, 0, null), Defaults);

        result.Level.Should().Be(OutboxHealthLevel.Healthy);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenDeadLetterExists()
    {
        var result = OutboxHealthEvaluator.Evaluate(new OutboxStatus(0, 1, null), Defaults);

        result.Level.Should().Be(OutboxHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_NotBeUnhealthy_WhenDeadLetterIgnoredByOption()
    {
        var options = new OutboxHealthOptions { DeadLetterIsUnhealthy = false };

        var result = OutboxHealthEvaluator.Evaluate(new OutboxStatus(0, 3, null), options);

        result.Level.Should().Be(OutboxHealthLevel.Healthy);
    }

    [Theory]
    [InlineData(119, OutboxHealthLevel.Healthy)]
    [InlineData(120, OutboxHealthLevel.Degraded)]
    [InlineData(599, OutboxHealthLevel.Degraded)]
    [InlineData(600, OutboxHealthLevel.Unhealthy)]
    public void Evaluate_Should_MapOldestPendingAgeToLevel(int ageSeconds, OutboxHealthLevel expected)
    {
        var status = new OutboxStatus(5, 0, TimeSpan.FromSeconds(ageSeconds));

        OutboxHealthEvaluator.Evaluate(status, Defaults).Level.Should().Be(expected);
    }

    [Theory]
    [InlineData(500, OutboxHealthLevel.Healthy)]
    [InlineData(501, OutboxHealthLevel.Degraded)]
    public void Evaluate_Should_DegradeOnlyWhenPendingExceedsThreshold(int pending, OutboxHealthLevel expected)
    {
        var status = new OutboxStatus(pending, 0, TimeSpan.FromSeconds(1));

        OutboxHealthEvaluator.Evaluate(status, Defaults).Level.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_Should_HonorCustomThresholds()
    {
        var options = new OutboxHealthOptions { DegradedAfterSeconds = 5, UnhealthyAfterSeconds = 10, PendingDegradedThreshold = 2 };

        OutboxHealthEvaluator.Evaluate(new OutboxStatus(1, 0, TimeSpan.FromSeconds(6)), options)
            .Level.Should().Be(OutboxHealthLevel.Degraded);
        OutboxHealthEvaluator.Evaluate(new OutboxStatus(1, 0, TimeSpan.FromSeconds(10)), options)
            .Level.Should().Be(OutboxHealthLevel.Unhealthy);
        OutboxHealthEvaluator.Evaluate(new OutboxStatus(3, 0, TimeSpan.FromSeconds(1)), options)
            .Level.Should().Be(OutboxHealthLevel.Degraded);
    }

    [Fact]
    public void Evaluate_Should_ExposeCountsAndAgeInData()
    {
        var result = OutboxHealthEvaluator.Evaluate(new OutboxStatus(4, 2, TimeSpan.FromSeconds(30)), Defaults);

        result.Data["pendingCount"].Should().Be(4);
        result.Data["deadLetterCount"].Should().Be(2);
        result.Data["oldestPendingAgeSeconds"].Should().Be(30d);
    }
}
