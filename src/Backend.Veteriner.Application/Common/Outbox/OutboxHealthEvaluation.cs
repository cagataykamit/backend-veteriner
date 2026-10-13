namespace Backend.Veteriner.Application.Common.Outbox;

public enum OutboxHealthLevel
{
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>Outbox tablosunun anlık özeti. Bekleyen = işlenmemiş ve dead-letter olmayan.</summary>
public sealed record OutboxStatus(int PendingCount, int DeadLetterCount, TimeSpan? OldestPendingAge);

public sealed record OutboxHealthEvaluation(
    OutboxHealthLevel Level,
    string Description,
    IReadOnlyDictionary<string, object?> Data);
