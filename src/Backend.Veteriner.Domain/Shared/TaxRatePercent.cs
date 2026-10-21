namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// KDV ve stopaj oranı kuralı (yüzde): 0–100, en çok 2 ondalık. Varsayılan KDV oranı yalnızca burada tanımlıdır;
/// mevzuat değişince tek yerden değişir (ADR-036 mimari ilke 2).
/// </summary>
public static class TaxRatePercent
{
    public const decimal DefaultVatRate = 20m;
    public const decimal Max = 100m;
    public const int Decimals = 2;
    public const string InvalidMessage = "Oran 0 ile 100 arasında ve en çok 2 ondalıklı olmalıdır.";

    public static bool IsValid(decimal rate)
        => rate >= 0 && rate <= Max && decimal.Round(rate, Decimals) == rate;

    public static bool IsValidOptional(decimal? rate)
        => rate is null || IsValid(rate.Value);
}
