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
}
