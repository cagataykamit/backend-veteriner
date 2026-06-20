namespace Backend.Veteriner.Application.Projections.Pets;

/// <summary>
/// Salt okunur pet projection kuyruk durumu (client/appointment muadiliyle hizalı, hosting-neutral).
/// </summary>
public sealed record PetProjectionStatus(
    int PendingCount,
    int RetryWaitingCount,
    int DeadLetterCount,
    DateTime? OldestPendingCreatedAtUtc,
    TimeSpan? OldestPendingAge,
    DateTime? NextRetryAtUtc,
    bool QueryDatabaseReachable,
    bool QueryDatabaseHasPendingMigrations,
    bool ProjectionEnabled);
