using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Appointments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Projections.Appointments;

public sealed class AppointmentProjectionHealthEvaluatorTests
{
    private static readonly AppointmentProjectionHealthOptions DefaultHealth = new()
    {
        DegradedAfterSeconds = 10,
        UnhealthyAfterSeconds = 60,
        DeadLetterIsUnhealthy = true
    };

    private static readonly QueryReadModelsOptions FlagsOff = new()
    {
        AppointmentsEnabled = false,
        DashboardAppointmentsEnabled = false
    };

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenNoPendingAndQueryReachable()
    {
        var status = CreateStatus(pendingCount: 0);

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagsOff);

        result.Level.Should().Be(AppointmentProjectionHealthLevel.Healthy);
    }

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenPendingAgeBelowDegradedThreshold()
    {
        var status = CreateStatus(pendingCount: 1, oldestPendingAge: TimeSpan.FromSeconds(5));

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagsOff);

        result.Level.Should().Be(AppointmentProjectionHealthLevel.Healthy);
    }

    [Fact]
    public void Evaluate_Should_BeDegraded_WhenPendingAgeAtDegradedThreshold()
    {
        var status = CreateStatus(pendingCount: 1, oldestPendingAge: TimeSpan.FromSeconds(10));

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagsOff);

        result.Level.Should().Be(AppointmentProjectionHealthLevel.Degraded);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenPendingAgeAtUnhealthyThreshold()
    {
        var status = CreateStatus(pendingCount: 1, oldestPendingAge: TimeSpan.FromSeconds(60));

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagsOff);

        result.Level.Should().Be(AppointmentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenDeadLetterExists()
    {
        var status = CreateStatus(deadLetterCount: 1);

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagsOff);

        result.Level.Should().Be(AppointmentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeDegraded_WhenReadFlagsOnAndProjectionDisabled()
    {
        var status = CreateStatus(projectionEnabled: false);
        var flags = new QueryReadModelsOptions { AppointmentsEnabled = true, DashboardAppointmentsEnabled = false };

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, flags);

        result.Level.Should().Be(AppointmentProjectionHealthLevel.Degraded);
        result.Data["appointmentsReadEnabled"].Should().Be(true);
    }

    [Fact]
    public void Evaluate_Should_BeUnhealthy_WhenReadFlagsOnProjectionDisabledAndPendingExists()
    {
        var status = CreateStatus(projectionEnabled: false, pendingCount: 2);
        var flags = new QueryReadModelsOptions { AppointmentsEnabled = false, DashboardAppointmentsEnabled = true };

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, flags);

        result.Level.Should().Be(AppointmentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Should_BeHealthy_WhenProjectionDisabledAndReadFlagsOff()
    {
        var status = CreateStatus(projectionEnabled: false, pendingCount: 3);

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagsOff);

        result.Level.Should().Be(AppointmentProjectionHealthLevel.Healthy);
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

        var result = AppointmentProjectionHealthEvaluator.Evaluate(status, DefaultHealth, FlagsOff);

        result.Data.Should().ContainKey("pendingCount");
        result.Data.Should().ContainKey("retryWaitingCount");
        result.Data.Should().ContainKey("deadLetterCount");
        result.Data.Should().ContainKey("oldestPendingAgeSeconds");
        result.Data.Should().ContainKey("projectionEnabled");
    }

    private static AppointmentProjectionStatus CreateStatus(
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

        return new AppointmentProjectionStatus(
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
