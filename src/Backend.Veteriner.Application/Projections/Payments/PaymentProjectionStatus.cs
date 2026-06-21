namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Salt okunur payment finance projection kuyruk durumu (client/pet muadiliyle hizalı).
/// </summary>
public sealed record PaymentProjectionStatus(
    int PendingCount,
    int RetryWaitingCount,
    int DeadLetterCount,
    DateTime? OldestPendingCreatedAtUtc,
    TimeSpan? OldestPendingAge,
    DateTime? NextRetryAtUtc,
    bool QueryDatabaseReachable,
    bool QueryDatabaseHasPendingMigrations,
    bool ProjectionEnabled);
