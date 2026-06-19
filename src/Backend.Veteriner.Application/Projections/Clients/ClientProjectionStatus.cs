namespace Backend.Veteriner.Application.Projections.Clients;

/// <summary>
/// Salt okunur client projection kuyruk durumu (appointment muadiliyle hizalı, hosting-neutral).
/// </summary>
public sealed record ClientProjectionStatus(
    int PendingCount,
    int RetryWaitingCount,
    int DeadLetterCount,
    DateTime? OldestPendingCreatedAtUtc,
    TimeSpan? OldestPendingAge,
    DateTime? NextRetryAtUtc,
    bool QueryDatabaseReachable,
    bool QueryDatabaseHasPendingMigrations,
    bool ProjectionEnabled);
