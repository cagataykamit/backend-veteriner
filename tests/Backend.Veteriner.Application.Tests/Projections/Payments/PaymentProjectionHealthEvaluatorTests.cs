using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Projections.Payments;

public sealed class PaymentProjectionHealthEvaluatorTests
{
    private static readonly PaymentProjectionHealthOptions DefaultHealth = new()
    {
        DegradedAfterSeconds = 10,
        UnhealthyAfterSeconds = 30,
        DeadLetterIsUnhealthy = true
    };

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenProjectionDisabled_EvenWithPendingAndDeadLetter()
    {
        var status = CreateStatus(
            pendingCount: 5,
            deadLetterCount: 2,
            oldestPendingAge: TimeSpan.FromMinutes(5),
            projectionEnabled: false);

        var result = PaymentProjectionHealthEvaluator.Evaluate(status, DefaultHealth);

        result.Level.Should().Be(PaymentProjectionHealthLevel.Healthy);
        result.Data["pendingCount"].Should().Be(5);
        result.Data["deadLetterCount"].Should().Be(2);
        result.Data["projectionEnabled"].Should().Be(false);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenQueryDbUnreachable()
    {
        var status = CreateStatus(queryReachable: false, projectionEnabled: true);

        var result = PaymentProjectionHealthEvaluator.Evaluate(status, DefaultHealth);

        result.Level.Should().Be(PaymentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeDegraded_WhenProjectionEnabledAndPendingAgeExceedsThreshold()
    {
        var status = CreateStatus(
            pendingCount: 1,
            oldestPendingAge: TimeSpan.FromSeconds(15),
            projectionEnabled: true);

        var result = PaymentProjectionHealthEvaluator.Evaluate(status, DefaultHealth);

        result.Level.Should().Be(PaymentProjectionHealthLevel.Degraded);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenProjectionEnabledAndDeadLetterExists()
    {
        var status = CreateStatus(deadLetterCount: 1, projectionEnabled: true);

        var result = PaymentProjectionHealthEvaluator.Evaluate(status, DefaultHealth);

        result.Level.Should().Be(PaymentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenProjectionEnabledAndQueueEmpty()
    {
        var status = CreateStatus(projectionEnabled: true);

        var result = PaymentProjectionHealthEvaluator.Evaluate(status, DefaultHealth);

        result.Level.Should().Be(PaymentProjectionHealthLevel.Healthy);
    }

    private static PaymentProjectionStatus CreateStatus(
        int pendingCount = 0,
        int retryWaitingCount = 0,
        int deadLetterCount = 0,
        TimeSpan? oldestPendingAge = null,
        bool queryReachable = true,
        bool pendingMigrations = false,
        bool projectionEnabled = false)
        => new(
            pendingCount,
            retryWaitingCount,
            deadLetterCount,
            oldestPendingAge is null ? null : DateTime.UtcNow.Add(-oldestPendingAge.Value),
            oldestPendingAge,
            null,
            queryReachable,
            pendingMigrations,
            projectionEnabled);
}
