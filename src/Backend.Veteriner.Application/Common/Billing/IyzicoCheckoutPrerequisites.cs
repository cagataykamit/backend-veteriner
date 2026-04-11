using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

public static class IyzicoCheckoutPrerequisites
{
    public static bool IsComplete(IyzicoBillingOptions iyzico)
    {
        if (string.IsNullOrWhiteSpace(iyzico.ApiKey))
            return false;
        if (string.IsNullOrWhiteSpace(iyzico.SecretKey))
            return false;
        if (string.IsNullOrWhiteSpace(iyzico.BaseUrl))
            return false;
        if (string.IsNullOrWhiteSpace(iyzico.CallbackUrl))
            return false;

        var prices = iyzico.PlanPricesTry;
        if (prices is null || prices.Count == 0)
            return false;

        foreach (var plan in SubscriptionPlanCatalog.All)
        {
            var code = SubscriptionPlanCatalog.ToApiCode(plan.Code);
            if (!TryGetPlanPrice(prices, code, out var amount) || amount <= 0m)
                return false;
        }

        return true;
    }

    public static bool TryGetPlanPrice(IReadOnlyDictionary<string, decimal>? prices, string planCode, out decimal amount)
    {
        amount = 0m;
        if (prices is null || prices.Count == 0)
            return false;

        if (prices.TryGetValue(planCode, out var exact) && exact > 0m)
        {
            amount = exact;
            return true;
        }

        foreach (var kv in prices)
        {
            if (string.Equals(kv.Key, planCode, StringComparison.OrdinalIgnoreCase) && kv.Value > 0m)
            {
                amount = kv.Value;
                return true;
            }
        }

        return false;
    }
}
