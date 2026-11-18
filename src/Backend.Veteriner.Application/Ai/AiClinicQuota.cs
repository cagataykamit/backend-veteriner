namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// Kliniğin bu ayki AI kredi durumu (ADR-044 madde 5–8); kota kuralının tek yeri. Önce aylık kota, sonra fon harcanır; aylık
/// kota devretmez, fon devreder. Token ve dolar sınırları kredi kotasının üstünde sigortadır: hangisi önce dolarsa AI durur.
/// </summary>
/// <param name="MonthlyCredits">Bu ayın kotası (paket hediyesi; eklenti açılınca eklenir).</param>
/// <param name="MonthQuotaCreditsUsed">Bu ay kotadan harcanan.</param>
/// <param name="MonthFundCreditsUsed">Bu ay fondan harcanan.</param>
/// <param name="FundBalance">Fon bakiyesi (eşzamanlı isteklerde az miktar eksiye düşebilir).</param>
/// <param name="ResetsAtUtc">Aylık kotanın yenileneceği an (sonraki İstanbul ayının başı).</param>
public sealed record AiClinicQuota(
    int MonthlyCredits,
    decimal MonthQuotaCreditsUsed,
    decimal MonthFundCreditsUsed,
    decimal FundBalance,
    long MonthTokens,
    decimal MonthCostUsd,
    long TokenLimit,
    decimal CostLimitUsd,
    DateTime MonthStartUtc,
    DateTime ResetsAtUtc)
{
    /// <summary>%80 uyarısı eşiği (ADR-044 madde 7).</summary>
    public const int WarningPercent = 80;

    public decimal QuotaRemaining => Math.Max(0m, MonthlyCredits - MonthQuotaCreditsUsed);

    public decimal FundRemaining => Math.Max(0m, FundBalance);

    public decimal CreditsRemaining => QuotaRemaining + FundRemaining;

    public decimal MonthCreditsUsed => MonthQuotaCreditsUsed + MonthFundCreditsUsed;

    private decimal CreditRatio
    {
        get
        {
            var total = MonthCreditsUsed + CreditsRemaining;
            return total > 0 ? MonthCreditsUsed / total : 1m;
        }
    }

    private decimal TokenRatio => TokenLimit > 0 ? (decimal)MonthTokens / TokenLimit : 1m;

    private decimal CostRatio => CostLimitUsd > 0 ? MonthCostUsd / CostLimitUsd : 1m;

    /// <summary>Kredi bitti ya da bir sigorta sınırı doldu: yalnız AI durur (ADR-044 madde 7).</summary>
    public bool IsExhausted => CreditsRemaining <= 0 || TokenRatio >= 1 || CostRatio >= 1;

    /// <summary>
    /// Bu ayın kullanım oranı (0–100): harcanan / (harcanan + kalan kredi), sigorta oranları daha yüksekse onlar. Fon eklenince
    /// oran düşer. Kliniğe dolar gösterilmez; yalnız bu oran ve kredi.
    /// </summary>
    public int UsedPercent => IsExhausted
        ? 100
        : (int)Math.Min(99m, Math.Floor(Math.Max(CreditRatio, Math.Max(TokenRatio, CostRatio)) * 100m));

    /// <summary>Verilmesi gereken uyarı eşiği: bitti 100, %80 ve üstü 80, değilse 0.</summary>
    public int WarningLevel => IsExhausted ? 100 : UsedPercent >= WarningPercent ? WarningPercent : 0;

    /// <summary>Harcanacak krediyi önce kalan kotaya, artanı fona böler (fon eksiye düşebilir; istek önceden onaylanmıştır).</summary>
    public (decimal FromQuota, decimal FromFund) Split(decimal credits)
    {
        if (credits <= 0)
            return (0m, 0m);

        var fromQuota = Math.Min(credits, QuotaRemaining);
        return (fromQuota, credits - fromQuota);
    }

    /// <summary>Harcamadan sonraki durum.</summary>
    public AiClinicQuota After(decimal fromQuota, decimal fromFund, long tokens, decimal costUsd) => this with
    {
        MonthQuotaCreditsUsed = MonthQuotaCreditsUsed + fromQuota,
        MonthFundCreditsUsed = MonthFundCreditsUsed + fromFund,
        FundBalance = FundBalance - fromFund,
        MonthTokens = MonthTokens + tokens,
        MonthCostUsd = MonthCostUsd + costUsd,
    };
}

/// <summary>İsteğin harcadığı kredi ve fondan karşılanan kısmı (kullanım kaydına yazılır).</summary>
public sealed record AiCreditCharge(decimal Credits, decimal FundCredits)
{
    public static readonly AiCreditCharge None = new(0m, 0m);
}
