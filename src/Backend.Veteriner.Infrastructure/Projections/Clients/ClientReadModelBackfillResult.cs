namespace Backend.Veteriner.Infrastructure.Projections.Clients;

/// <summary>
/// Client read-model backfill/rebuild sonucu. PII içermez (yalnızca sayım/zaman).
/// </summary>
public sealed record ClientReadModelBackfillResult(
    bool Success,
    Guid? ScopeTenantId,
    long CommandClientCount,
    long QueryClientCount,
    long InsertedCount,
    long UpdatedCount,
    long SkippedStaleCount,
    bool ParityInSync,
    TimeSpan Duration);
