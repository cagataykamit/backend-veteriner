namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Command DB <c>Payments</c> kayıtlarından Query DB finance contribution + daily stats idempotent backfill.
/// </summary>
public interface IPaymentFinanceBackfillService
{
    Task<PaymentFinanceBackfillResult> BackfillAsync(
        Guid? tenantId = null,
        int batchSize = PaymentFinanceBackfillService.DefaultBatchSize,
        CancellationToken cancellationToken = default);
}
