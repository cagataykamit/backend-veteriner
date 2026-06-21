namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Command DB <c>Payments</c> kayıtlarını Query DB <c>PaymentReadModels</c> (list/search read-model) tablosuna
/// idempotent biçimde dolduran/yeniden oluşturan backfill servisi (CQRS-14F).
/// </summary>
public interface IPaymentReadModelBackfillService
{
    /// <summary>
    /// Backfill çalıştırır.
    /// </summary>
    /// <param name="tenantId">
    /// Verilirse yalnızca o tenant'ın ödemeleri işlenir (tenant izolasyonu). <c>null</c> ise tüm tenant'lar.
    /// </param>
    /// <param name="batchSize">Command DB'den tek seferde okunacak satır sayısı.</param>
    Task<PaymentReadModelBackfillResult> BackfillAsync(
        Guid? tenantId = null,
        int batchSize = PaymentReadModelBackfillService.DefaultBatchSize,
        CancellationToken cancellationToken = default);
}
