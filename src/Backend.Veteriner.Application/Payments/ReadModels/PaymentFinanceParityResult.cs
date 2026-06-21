namespace Backend.Veteriner.Application.Payments.ReadModels;

/// <summary>
/// Command DB <c>Payments</c> ile Query DB contribution + daily stats parity sonucu.
/// </summary>
public sealed record PaymentFinanceParityResult(
    long CommandPaymentCount,
    long QueryContributionCount,
    bool CountInSync,
    bool DailyBucketParityInSync,
    int DailyBucketMismatchCount,
    IReadOnlyList<PaymentFinanceDailyBucketMismatch> DailyBucketMismatches,
    Guid? ScopeTenantId = null)
{
    public long CountDifference => CommandPaymentCount - QueryContributionCount;

    public long AbsoluteCountDifference => Math.Abs(CountDifference);

    public bool InSync => CountInSync && DailyBucketParityInSync;
}
