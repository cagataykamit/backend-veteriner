using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Infrastructure.Billing;

public sealed class ManualBillingCheckoutProvider : IBillingCheckoutProvider
{
    public BillingProvider Provider => BillingProvider.Manual;

    public Task<Result<CheckoutPrepareResult>> PrepareCheckoutAsync(
        BillingCheckoutSession session,
        string? chargeCurrencyCode,
        long? chargeAmountMinor,
        decimal? prorationRatio,
        CancellationToken ct = default)
        => Task.FromResult(Result<CheckoutPrepareResult>.Success(
            new CheckoutPrepareResult(null, null, chargeCurrencyCode, chargeAmountMinor, prorationRatio)));

    /// <summary>Manuel oturumun sağlayıcı sayfası yoktur; açık olduğu sürece kullanılabilir.</summary>
    public Task<CheckoutPageState> GetCheckoutPageStateAsync(BillingCheckoutSession session, CancellationToken ct = default)
        => Task.FromResult(CheckoutPageState.Payable);

    /// <summary>Manuel oturum sağlayıcıdan doğrulanmadığı için çift ödeme olarak işaretlenmez.</summary>
    public Task<ProviderRefundResult> RefundPaymentAsync(BillingCheckoutSession session, CancellationToken ct = default)
        => Task.FromResult(ProviderRefundResult.NotSupported("Manuel sağlayıcıda tahsilat yok."));
}
