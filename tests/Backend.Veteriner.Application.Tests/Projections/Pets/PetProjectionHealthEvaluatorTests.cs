using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Pets;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Projections.Pets;

public sealed class PetProjectionHealthEvaluatorTests
{
    private static readonly PetProjectionHealthOptions DefaultHealth = new()
    {
        DegradedAfterSeconds = 10,
        UnhealthyAfterSeconds = 30,
        DeadLetterIsUnhealthy = true
    };

    private static readonly QueryReadModelsOptions FlagOff = new() { PetsEnabled = false };

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenNoPendingAndQueryReachable()
    {
        var status = CreateStatus(pendingCount: 0);

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Healthy);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenQueryDbUnreachable()
    {
        var status = CreateStatus(queryReachable: false);

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenPendingMigrations()
    {
        var status = CreateStatus(pendingMigrations: true);

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenPendingAgeBelowDegradedThreshold()
    {
        var status = CreateStatus(pendingCount: 1, oldestPendingAge: TimeSpan.FromSeconds(5));

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Healthy);
    }

    [Fact]
    public void Evaluate_Should_BeDegraded_WhenPendingAgeAtDegradedThreshold()
    {
        var status = CreateStatus(pendingCount: 1, oldestPendingAge: TimeSpan.FromSeconds(10));

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Degraded);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenPendingAgeAtUnhealthyThreshold()
    {
        var status = CreateStatus(pendingCount: 1, oldestPendingAge: TimeSpan.FromSeconds(30));

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeDegraded_WhenRetryWaitingExists()
    {
        var status = CreateStatus(retryWaitingCount: 1);

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Degraded);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenDeadLetterExists()
    {
        var status = CreateStatus(deadLetterCount: 1);

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeDegraded_WhenReadFlagOnAndProjectionDisabledWithoutPending()
    {
        var status = CreateStatus(projectionEnabled: false);
        var flags = new QueryReadModelsOptions { PetsEnabled = true };

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, flags);

        result.Level.Should().Be(PetProjectionHealthLevel.Degraded);
        result.Data["petsReadEnabled"].Should().Be(true);
        result.Data["projectionEnabled"].Should().Be(false);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenReadFlagOnProjectionDisabledAndPendingExists()
    {
        var status = CreateStatus(projectionEnabled: false, pendingCount: 2);
        var flags = new QueryReadModelsOptions { PetsEnabled = true };

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, flags);

        result.Level.Should().Be(PetProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenProjectionDisabledAndReadFlagOff()
    {
        var status = CreateStatus(projectionEnabled: false, pendingCount: 3);

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Level.Should().Be(PetProjectionHealthLevel.Healthy);
    }

    [Fact]
    public void Evaluate_Should_IncludeOperationalDataPayload()
    {
        var status = CreateStatus(
            pendingCount: 2,
            retryWaitingCount: 1,
            deadLetterCount: 0,
            oldestPendingAge: TimeSpan.FromSeconds(3),
            nextRetryAtUtc: DateTime.UtcNow.AddMinutes(1));

        var result = PetProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagOff);

        result.Data.Should().ContainKey("pendingCount");
        result.Data.Should().ContainKey("retryWaitingCount");
        result.Data.Should().ContainKey("deadLetterCount");
        result.Data.Should().ContainKey("oldestPendingAgeSeconds");
        result.Data.Should().ContainKey("projectionEnabled");
        result.Data.Should().ContainKey("petsReadEnabled");
    }

    private static PetProjectionStatus CreateStatus(
        int pendingCount = 0,
        int retryWaitingCount = 0,
        int deadLetterCount = 0,
        TimeSpan? oldestPendingAge = null,
        DateTime? nextRetryAtUtc = null,
        bool queryReachable = true,
        bool pendingMigrations = false,
        bool projectionEnabled = true)
    {
        DateTime? oldestCreated = oldestPendingAge is { } age
            ? DateTime.UtcNow - age
            : null;

        return new PetProjectionStatus(
            pendingCount,
            retryWaitingCount,
            deadLetterCount,
            oldestCreated,
            oldestPendingAge,
            nextRetryAtUtc,
            queryReachable,
            pendingMigrations,
            projectionEnabled);
    }
}
