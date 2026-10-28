using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>Satır içi checkout URL / provider referansı üretimi (Stripe Checkout Session vb.).</summary>
public interface IBillingCheckoutProvider
{
    BillingProvider Provider { get; }

    Task<Result<CheckoutPrepareResult>> PrepareCheckoutAsync(
        BillingCheckoutSession session,
        string? chargeCurrencyCode,
        long? chargeAmountMinor,
        decimal? prorationRatio,
        CancellationToken ct = default);

    /// <summary>
    /// Açık oturumun ödeme sayfası hâlâ ödenebilir mi? Yeniden kullanmadan önce sorulur: sağlayıcı sayfayı (ör. iyzico'da
    /// ödeme denemesi sonrası) bildirmeden tüketmiş olabilir. Emin olunamazsa <see cref="CheckoutPageState.Unusable"/>.
    /// </summary>
    Task<CheckoutPageState> GetCheckoutPageStateAsync(BillingCheckoutSession session, CancellationToken ct = default);

    /// <summary>Oturumun tahsil edilmiş ödemesini sağlayıcıda tamamen geri verir (çift ödeme, BILL-005).</summary>
    Task<ProviderRefundResult> RefundPaymentAsync(BillingCheckoutSession session, CancellationToken ct = default);
}
