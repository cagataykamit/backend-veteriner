namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>
/// Belge tutarı kuralı tek yerde: ücret tutarı müşterinin ödediği (KDV dahil) tutardır; belgeye KDV hariç tutar gider.
/// Stopajlı e-SMM'de ödenen = brüt × (1 + KDV − stopaj) olduğundan brüt = ödenen / (1 + KDV − stopaj).
/// </summary>
public static class EDocumentAmounts
{
    public const int UnitPriceDecimals = 6;

    /// <summary>KDV (ve uygulanıyorsa stopaj) hariç satır tutarı, 2 ondalık.</summary>
    public static decimal NetLineAmount(decimal grossAmount, decimal vatRatePercent, decimal? withholdingRatePercent)
    {
        var divisor = 1m + vatRatePercent / 100m - (withholdingRatePercent ?? 0m) / 100m;
        if (divisor <= 0m)
            throw new ArgumentOutOfRangeException(nameof(withholdingRatePercent), "KDV ve stopaj oranı birlikte geçersiz.");
        return decimal.Round(grossAmount / divisor, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Satır tutarının miktara bölümü; en çok <see cref="UnitPriceDecimals"/> ondalık.</summary>
    public static decimal NetUnitPrice(decimal netLineAmount, decimal quantity)
    {
        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        return decimal.Round(netLineAmount / quantity, UnitPriceDecimals, MidpointRounding.AwayFromZero);
    }
}
