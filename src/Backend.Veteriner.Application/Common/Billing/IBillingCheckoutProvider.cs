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
}
