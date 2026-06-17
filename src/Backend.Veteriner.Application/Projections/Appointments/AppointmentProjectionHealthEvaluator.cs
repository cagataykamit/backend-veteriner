using Backend.Veteriner.Application.Common.Options;

namespace Backend.Veteriner.Application.Projections.Appointments;

/// <summary>
/// Appointment projection health kurallarını tek yerde toplar (test edilebilir, hosting-neutral).
/// </summary>
public static class AppointmentProjectionHealthEvaluator
{
    public static AppointmentProjectionHealthEvaluation Evaluate(
        AppointmentProjectionStatus status,
        AppointmentProjectionHealthOptions healthOptions,
        QueryReadModelsOptions queryReadModelsOptions)
    {
        var data = BuildData(status, queryReadModelsOptions);

        if (!status.QueryDatabaseReachable)
            return Unhealthy("Query SQL Server bağlantısı başarısız.", data);

        if (status.QueryDatabaseHasPendingMigrations)
            return Unhealthy("Query DB bekleyen migration var.", data);

        if (status.DeadLetterCount > 0 && healthOptions.DeadLetterIsUnhealthy)
            return Unhealthy($"Appointment projection dead-letter count: {status.DeadLetterCount}.", data);

        var readFlagsEnabled = queryReadModelsOptions.AppointmentsEnabled
            || queryReadModelsOptions.DashboardAppointmentsEnabled;

        if (!status.ProjectionEnabled && readFlagsEnabled)
        {
            if (status.PendingCount > 0 || status.RetryWaitingCount > 0)
                return Unhealthy("Query read flags enabled but projection disabled with pending appointment events.", data);

            return Degraded("Query read flags enabled but appointment projection disabled.", data);
        }

        if (status.PendingCount > 0 && status.OldestPendingAge is { } age)
        {
            var ageSeconds = age.TotalSeconds;
            if (ageSeconds >= healthOptions.UnhealthyAfterSeconds)
                return Unhealthy($"Oldest pending appointment event age {ageSeconds:F0}s.", data);

            if (ageSeconds >= healthOptions.DegradedAfterSeconds)
                return Degraded($"Oldest pending appointment event age {ageSeconds:F0}s.", data);
        }

        if (status.RetryWaitingCount > 0)
            return Degraded($"Appointment projection retry-waiting count: {status.RetryWaitingCount}.", data);

        return Healthy("Appointment projection queue healthy.", data);
    }

    private static IReadOnlyDictionary<string, object?> BuildData(
        AppointmentProjectionStatus status,
        QueryReadModelsOptions queryReadModelsOptions)
        => new Dictionary<string, object?>
        {
            ["pendingCount"] = status.PendingCount,
            ["retryWaitingCount"] = status.RetryWaitingCount,
            ["deadLetterCount"] = status.DeadLetterCount,
            ["oldestPendingAgeSeconds"] = status.OldestPendingAge?.TotalSeconds ?? 0d,
            ["nextRetryAtUtc"] = status.NextRetryAtUtc?.ToString("O") ?? string.Empty,
            ["projectionEnabled"] = status.ProjectionEnabled,
            ["appointmentsReadEnabled"] = queryReadModelsOptions.AppointmentsEnabled,
            ["dashboardReadEnabled"] = queryReadModelsOptions.DashboardAppointmentsEnabled
        };

    private static AppointmentProjectionHealthEvaluation Healthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(AppointmentProjectionHealthLevel.Healthy, description, data);

    private static AppointmentProjectionHealthEvaluation Degraded(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(AppointmentProjectionHealthLevel.Degraded, description, data);

    private static AppointmentProjectionHealthEvaluation Unhealthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(AppointmentProjectionHealthLevel.Unhealthy, description, data);
}
