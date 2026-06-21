namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Payment finance backfill sırasında tek bir Command DB <c>Payments</c> satırı için alınacak karar.
/// </summary>
public enum PaymentFinanceBackfillAction
{
    Insert,
    Update,
    SkipStale
}
