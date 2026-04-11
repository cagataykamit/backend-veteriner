using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Backend.Veteriner.Infrastructure.Billing;

/// <summary>
/// Stripe Checkout Session (subscription mode) oluşturur; metadata webhook ile uyumludur.
/// </summary>
public sealed class StripeBillingCheckoutProvider : IBillingCheckoutProvider
{
    private readonly IOptions<BillingOptions> _billingOptions;
    private readonly ILogger<StripeBillingCheckoutProvider> _logger;

    public StripeBillingCheckoutProvider(
        IOptions<BillingOptions> billingOptions,
        ILogger<StripeBillingCheckoutProvider> logger)
    {
        _billingOptions = billingOptions;
        _logger = logger;
    }

    public BillingProvider Provider => BillingProvider.Stripe;

    public async Task<Result<CheckoutPrepareResult>> PrepareCheckoutAsync(
        BillingCheckoutSession session,
        string? chargeCurrencyCode,
        long? chargeAmountMinor,
        decimal? prorationRatio,
        CancellationToken ct = default)
    {
        var stripeOpts = _billingOptions.Value.Stripe;
        var secret = stripeOpts.SecretKey.Trim();
        if (string.IsNullOrEmpty(secret))
        {
            return Result<CheckoutPrepareResult>.Failure(
                "Billing.StripeSecretMissing",
                "Stripe Checkout için Billing:Stripe:SecretKey yapılandırılmalı.");
        }

        var successUrl = stripeOpts.SuccessUrl.Trim();
        var cancelUrl = stripeOpts.CancelUrl.Trim();
        if (string.IsNullOrEmpty(successUrl) || string.IsNullOrEmpty(cancelUrl))
        {
            return Result<CheckoutPrepareResult>.Failure(
                "Billing.StripeCheckoutUrlsMissing",
                "Billing:Stripe:SuccessUrl ve Billing:Stripe:CancelUrl tanımlanmalı. SuccessUrl içinde {CHECKOUT_SESSION_ID} kullanılabilir.");
        }

        var targetApi = SubscriptionPlanCatalog.ToApiCode(session.TargetPlanCode);
        if (!TryGetSubscriptionPriceId(stripeOpts.SubscriptionPriceIds, targetApi, out var priceId))
        {
            return Result<CheckoutPrepareResult>.Failure(
                "Billing.StripePriceNotConfigured",
                $"Stripe için hedef plana ait recurring Price Id yok: {targetApi}. Billing:Stripe:SubscriptionPriceIds içine ekleyin.");
        }

        var currentApi = SubscriptionPlanCatalog.ToApiCode(session.CurrentPlanCode);
        var metadata = new Dictionary<string, string>
        {
            ["billing_checkout_session_id"] = session.Id.ToString("D"),
            ["tenant_id"] = session.TenantId.ToString("D"),
            ["target_plan_code"] = targetApi,
            ["current_plan_code"] = currentApi,
        };
        if (chargeAmountMinor.HasValue)
            metadata["prorated_charge_minor"] = chargeAmountMinor.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(chargeCurrencyCode))
            metadata["proration_currency"] = chargeCurrencyCode!;
        if (prorationRatio.HasValue)
            metadata["proration_ratio"] = prorationRatio.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = session.Id.ToString("D"),
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            Metadata = metadata,
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata },
        };

        if (session.ExpiresAtUtc.HasValue)
        {
            options.ExpiresAt = DateTime.SpecifyKind(session.ExpiresAtUtc.Value, DateTimeKind.Utc);
        }

        var requestOptions = new RequestOptions { ApiKey = secret };

        try
        {
            var service = new SessionService();
            var checkout = await service.CreateAsync(options, requestOptions, ct);

            if (string.IsNullOrWhiteSpace(checkout.Url))
            {
                return Result<CheckoutPrepareResult>.Failure(
                    "Billing.StripeApiError",
                    "Stripe Checkout URL dönmedi.");
            }

            return Result<CheckoutPrepareResult>.Success(
                new CheckoutPrepareResult(
                    checkout.Url.Trim(),
                    checkout.Id,
                    chargeCurrencyCode,
                    chargeAmountMinor,
                    prorationRatio));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe Checkout Session oluşturulamadı (plan {TargetPlan}).", targetApi);
            var detail = ex.StripeError?.Message ?? ex.Message;
            return Result<CheckoutPrepareResult>.Failure(
                "Billing.StripeApiError",
                string.IsNullOrWhiteSpace(detail) ? "Stripe API hatası." : detail);
        }
    }

    private static bool TryGetSubscriptionPriceId(
        IReadOnlyDictionary<string, string>? map,
        string planApiCode,
        out string priceId)
    {
        priceId = "";
        if (map is null || map.Count == 0)
            return false;

        if (map.TryGetValue(planApiCode, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            priceId = exact.Trim();
            return true;
        }

        foreach (var kv in map)
        {
            if (string.Equals(kv.Key, planApiCode, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(kv.Value))
            {
                priceId = kv.Value.Trim();
                return true;
            }
        }

        return false;
    }
}
