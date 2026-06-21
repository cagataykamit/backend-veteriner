namespace Backend.Veteriner.Application.Payments.ReadModels;

/// <summary>
/// Command DB <c>Payments</c> ile Query DB <c>PaymentReadModels</c> (list read-model) parity okuması (CQRS-14F,
/// operasyonel rollout gözlemi). Tenant + clinic kapsamlıdır (list read yüzeyi ile aynı kapsam).
/// </summary>
public interface IPaymentReadModelParityReader
{
    /// <summary>
    /// Verilen tenant + clinic kapsamında count + row-sample + recent ordering parity üretir.
    /// </summary>
    /// <param name="recentSampleSize">Recent top-N örnek boyutu (PaidAtUtc DESC, PaymentId DESC).</param>
    Task<PaymentReadModelParityResult> GetClinicParityAsync(
        Guid tenantId,
        Guid clinicId,
        int recentSampleSize = PaymentReadModelParityDefaults.RecentSampleSize,
        CancellationToken cancellationToken = default);
}

/// <summary>Payment list read-model parity varsayılanları.</summary>
public static class PaymentReadModelParityDefaults
{
    public const int RecentSampleSize = 50;
}
