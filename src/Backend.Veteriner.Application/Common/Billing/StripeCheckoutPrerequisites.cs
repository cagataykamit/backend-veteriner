using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>
/// Stripe Checkout Session oluşturmadan önce yapılandırmanın yeterli olup olmadığını kontrol eder.
/// </summary>
public static class StripeCheckoutPrerequisites
{
    public static bool IsComplete(StripeBillingOptions stripe)
    {
        if (string.IsNullOrWhiteSpace(stripe.SecretKey?.Trim()))
            return false;
        if (string.IsNullOrWhiteSpace(stripe.SuccessUrl?.Trim()))
            return false;
        if (string.IsNullOrWhiteSpace(stripe.CancelUrl?.Trim()))
            return false;

        var ids = stripe.SubscriptionPriceIds;
        if (ids is null || ids.Count == 0)
            return false;

        foreach (var plan in SubscriptionPlanCatalog.All)
        {
            var code = SubscriptionPlanCatalog.ToApiCode(plan.Code);
            if (!TryGetPriceId(ids, code, out var priceId) || string.IsNullOrWhiteSpace(priceId))
                return false;
        }

        return true;
    }

    private static bool TryGetPriceId(
        IReadOnlyDictionary<string, string> map,
        string planApiCode,
        out string priceId)
    {
        priceId = "";
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
