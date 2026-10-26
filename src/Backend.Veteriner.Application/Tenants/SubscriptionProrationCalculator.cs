namespace Backend.Veteriner.Application.Tenants;

public sealed record SubscriptionProrationResult(
    decimal ProrationRatio,
    long PriceDiffMinor,
    long ProratedChargeMinor);

public static class SubscriptionProrationCalculator
{
    public static SubscriptionProrationResult Calculate(
        DateTime utcNow,
        DateTime currentPeriodStartUtc,
        DateTime currentPeriodEndUtc,
        long currentPlanPriceMinor,
        long targetPlanPriceMinor)
    {
        var now = EnsureUtc(utcNow);
        var start = EnsureUtc(currentPeriodStartUtc);
        var end = EnsureUtc(currentPeriodEndUtc);
        if (end <= start)
            throw new ArgumentException("CurrentPeriodEndUtc, CurrentPeriodStartUtc'ten büyük olmalıdır.");

        var totalSeconds = (decimal)(end - start).TotalSeconds;
        var remainingSeconds = (decimal)(end - now).TotalSeconds;
        if (remainingSeconds < 0)
            remainingSeconds = 0;
        if (remainingSeconds > totalSeconds)
            remainingSeconds = totalSeconds;

        var ratio = totalSeconds <= 0 ? 0m : remainingSeconds / totalSeconds;
        var diff = targetPlanPriceMinor - currentPlanPriceMinor;
        var charge = (long)Math.Round(diff * ratio, MidpointRounding.AwayFromZero);

        return new SubscriptionProrationResult(ratio, diff, charge);
    }

    /// <summary>
    /// Dönem türü değişimi (aylıktan yıllığa, BILL-001): yeni dönem hemen başlar; mevcut dönemin kullanılmamış kısmı
    /// (<paramref name="currentPeriodPriceMinor"/> × kalan oran) yeni dönem fiyatından düşülür. Oran, mevcut dönemden kalan paydır.
    /// </summary>
    public static SubscriptionProrationResult CalculatePeriodSwitch(
        DateTime utcNow,
        DateTime currentPeriodStartUtc,
        DateTime currentPeriodEndUtc,
        long currentPeriodPriceMinor,
        long newPeriodPriceMinor)
    {
        var unused = Calculate(utcNow, currentPeriodStartUtc, currentPeriodEndUtc, 0, currentPeriodPriceMinor);
        var charge = newPeriodPriceMinor - unused.ProratedChargeMinor;

        return new SubscriptionProrationResult(unused.ProrationRatio, newPeriodPriceMinor - currentPeriodPriceMinor, charge);
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
