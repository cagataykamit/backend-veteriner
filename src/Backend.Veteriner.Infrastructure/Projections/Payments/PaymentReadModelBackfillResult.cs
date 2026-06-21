namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Payment list read-model backfill sonucu (CQRS-14F). PII içermez (yalnızca sayım/zaman).
/// </summary>
public sealed record PaymentReadModelBackfillResult(
    bool Success,
    Guid? ScopeTenantId,
    long CommandPaymentCount,
    long QueryReadModelCount,
    long InsertedCount,
    long UpdatedCount,
    long SkippedStaleCount,
    bool ParityInSync,
    TimeSpan Duration);
