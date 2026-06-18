namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed record AppointmentProjectionMetricsSnapshot(
    int PendingCount,
    int RetryWaitingCount,
    int DeadLetterCount,
    double OldestPendingAgeSeconds,
    int ProjectionEnabled,
    int AppointmentsQueryReadEnabled,
    int DashboardQueryReadEnabled,
    int QueryDatabaseHealthy,
    string Mode)
{
    public static AppointmentProjectionMetricsSnapshot Empty { get; } = new(
        PendingCount: 0,
        RetryWaitingCount: 0,
        DeadLetterCount: 0,
        OldestPendingAgeSeconds: 0,
        ProjectionEnabled: 0,
        AppointmentsQueryReadEnabled: 0,
        DashboardQueryReadEnabled: 0,
        QueryDatabaseHealthy: 0,
        Mode: "command-read");
}
