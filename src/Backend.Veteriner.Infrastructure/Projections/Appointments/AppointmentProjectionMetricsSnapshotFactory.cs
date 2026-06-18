using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Appointments;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public static class AppointmentProjectionMetricsSnapshotFactory
{
    public static AppointmentProjectionMetricsSnapshot Create(
        AppointmentProjectionStatus status,
        QueryReadModelsOptions queryReadModelsOptions)
    {
        var oldestPendingAgeSeconds = status.OldestPendingAge?.TotalSeconds ?? 0d;
        if (oldestPendingAgeSeconds < 0)
            oldestPendingAgeSeconds = 0;

        var queryHealthy = status.QueryDatabaseReachable && !status.QueryDatabaseHasPendingMigrations;

        return new AppointmentProjectionMetricsSnapshot(
            status.PendingCount,
            status.RetryWaitingCount,
            status.DeadLetterCount,
            oldestPendingAgeSeconds,
            status.ProjectionEnabled ? 1 : 0,
            queryReadModelsOptions.AppointmentsEnabled ? 1 : 0,
            queryReadModelsOptions.DashboardAppointmentsEnabled ? 1 : 0,
            queryHealthy ? 1 : 0,
            AppointmentProjectionModeTags.Resolve(queryReadModelsOptions));
    }
}
