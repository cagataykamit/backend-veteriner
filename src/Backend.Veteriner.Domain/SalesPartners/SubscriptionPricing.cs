namespace Backend.Veteriner.Domain.SalesPartners;

/// <summary>
/// Abonelik tahsilat tutarının dökümü (kuruş, KDV hariç). <see cref="AmountBeforeCodeMinor"/> kod indirimi öncesi ("brüt"),
/// <see cref="NetAmountMinor"/> tüm indirimler sonrası çekilen tutar ve komisyon tabanıdır.
/// </summary>
public sealed record SubscriptionPriceBreakdown(long ListAmountMinor, long PeriodDiscountMinor, long CodeDiscountMinor)
{
    public long AmountBeforeCodeMinor => ListAmountMinor - PeriodDiscountMinor;
    public long NetAmountMinor => AmountBeforeCodeMinor - CodeDiscountMinor;
}

/// <summary>
/// Abonelik fiyatı hesabının tek yeri (ADR-035, ADR-037; kullanıcı kararı 2026-10-22 "B"). Sıra:
/// plan fiyatı → yıllık indirim (dönem yıllıksa: 12 ay yerine <see cref="YearlyBilledMonths"/> ay) → satış ortağı kodu
/// indirimi (yüzde, kalan tutara; <see cref="DiscountRedemption"/> uygunluk kuralıyla) → net.
/// Faturalandırma altyapısı bugün yalnız aylık dönem üretir; yıllık hesap hazırdır.
/// </summary>
public static class SubscriptionPricing
{
    public const int MonthsPerYear = 12;

    /// <summary>Yıllık ödemede ücretlendirilen ay sayısı ("2 ay hediye").</summary>
    public const int YearlyBilledMonths = 10;

    /// <summary>Plan aylık fiyatından dönem tutarı ve indirimleri hesaplar.</summary>
    public static SubscriptionPriceBreakdown Calculate(
        long monthlyPriceMinor,
        BillingPeriodKind period,
        DiscountRedemption? redemption,
        DateTime pricedAtUtc)
    {
        if (monthlyPriceMinor <= 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyPriceMinor), "Aylık fiyat pozitif olmalı.");

        var (list, periodDiscount) = period switch
        {
            BillingPeriodKind.Monthly => (monthlyPriceMinor, 0L),
            BillingPeriodKind.Yearly => (monthlyPriceMinor * MonthsPerYear, monthlyPriceMinor * (MonthsPerYear - YearlyBilledMonths)),
            _ => throw new ArgumentOutOfRangeException(nameof(period)),
        };

        var beforeCode = list - periodDiscount;
        var codeDiscount = redemption?.CalculateDiscountMinor(beforeCode, pricedAtUtc, period) ?? 0;
        return new SubscriptionPriceBreakdown(list, periodDiscount, codeDiscount);
    }

    /// <summary>Plan fiyatından türemeyen tutar (ör. yükseltme farkı): dönem indirimi yok, yalnız kod indirimi.</summary>
    public static SubscriptionPriceBreakdown ForAmount(
        long amountMinor,
        BillingPeriodKind period,
        DiscountRedemption? redemption,
        DateTime pricedAtUtc)
    {
        if (amountMinor <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountMinor), "Tutar pozitif olmalı.");

        var codeDiscount = redemption?.CalculateDiscountMinor(amountMinor, pricedAtUtc, period) ?? 0;
        return new SubscriptionPriceBreakdown(amountMinor, 0, codeDiscount);
    }
}
