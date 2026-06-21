namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Payment finance backfill sonucu. PII içermez (yalnızca sayım/zaman).
/// </summary>
public sealed record PaymentFinanceBackfillResult(
    bool Success,
    Guid? ScopeTenantId,
    long CommandPaymentCount,
    long QueryContributionCount,
    long InsertedCount,
    long UpdatedCount,
    long SkippedStaleCount,
    long RecomputedBucketCount,
    bool CountParityInSync,
    bool DailyBucketParityInSync,
    TimeSpan Duration);
