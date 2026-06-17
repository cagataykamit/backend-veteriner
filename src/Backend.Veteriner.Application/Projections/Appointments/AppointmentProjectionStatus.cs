namespace Backend.Veteriner.Application.Projections.Appointments;

/// <summary>
/// Salt okunur appointment projection kuyruk durumu.
/// </summary>
public sealed record AppointmentProjectionStatus(
    int PendingCount,
    int RetryWaitingCount,
    int DeadLetterCount,
    DateTime? OldestPendingCreatedAtUtc,
    TimeSpan? OldestPendingAge,
    DateTime? NextRetryAtUtc,
    bool QueryDatabaseReachable,
    bool QueryDatabaseHasPendingMigrations,
    bool ProjectionEnabled);
