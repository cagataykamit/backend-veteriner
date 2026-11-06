using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>
/// Plan liste fiyatının tek okunduğu yer (WEB-007): checkout tutarı, panel abonelik kartı ve tanıtım sitesi paketler sayfası
/// aynı kuralla okur, böylece gösterilen fiyat ile çekilen tutar ayrışmaz. Kaynak yapılandırmadır (aylık, kuruş, KDV hariç):
/// önce <see cref="BillingOptions.PlanPricesMinor"/>, yoksa <see cref="IyzicoBillingOptions.PlanPricesTry"/>. Sıfır ya da eksik
/// değer "fiyat yapılandırılmamış" sayılır.
/// </summary>
public static class SubscriptionPlanPrices
{
    /// <summary>Plan fiyatları KDV hariçtir; ödeme tutarına KDV eklenmez (SALES_PARTNER_DISCOUNT_API_CONTRACT).</summary>
    public const bool VatIncluded = false;

    public static bool TryGetMonthlyPriceMinor(BillingOptions options, SubscriptionPlanCode code, out long minor)
    {
        var apiCode = SubscriptionPlanCatalog.ToApiCode(code);
        if (TryGetValue(options.PlanPricesMinor, apiCode, out minor) && minor > 0)
            return true;

        if (TryGetValue(options.Iyzico.PlanPricesTry, apiCode, out var amountTry))
        {
            minor = (long)Math.Round(amountTry * 100m, MidpointRounding.AwayFromZero);
            if (minor > 0)
                return true;
        }

        minor = 0;
        return false;
    }

    /// <summary>Dönem fiyatı (kod indirimi öncesi; yıllıkta <see cref="SubscriptionPricing.YearlyBilledMonths"/> ay); fiyat yoksa <c>null</c>.</summary>
    public static long? PeriodPriceMinorOrNull(BillingOptions options, SubscriptionPlanCode code, BillingPeriodKind period)
        => TryGetMonthlyPriceMinor(options, code, out var monthly)
            ? SubscriptionPricing.PeriodPriceMinor(monthly, period)
            : null;

    /// <summary>Fiyat para birimi (ISO-4217, büyük harf); boşsa TRY.</summary>
    public static string Currency(BillingOptions options)
    {
        var configured = options.PlanPriceCurrency?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? "TRY" : configured.ToUpperInvariant();
    }

    private static bool TryGetValue<T>(IReadOnlyDictionary<string, T>? map, string key, out T value)
    {
        value = default!;
        if (map is null || map.Count == 0)
            return false;

        if (map.TryGetValue(key, out value!))
            return true;

        foreach (var pair in map)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        return false;
    }
}
