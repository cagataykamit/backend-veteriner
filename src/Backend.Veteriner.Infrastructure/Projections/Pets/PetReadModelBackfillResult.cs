namespace Backend.Veteriner.Infrastructure.Projections.Pets;

/// <summary>
/// Pet read-model backfill/rebuild sonucu. PII içermez (yalnızca sayım/zaman).
/// </summary>
public sealed record PetReadModelBackfillResult(
    bool Success,
    Guid? ScopeTenantId,
    long CommandPetCount,
    long QueryPetCount,
    long InsertedCount,
    long UpdatedCount,
    long SkippedStaleCount,
    bool ParityInSync,
    TimeSpan Duration);
