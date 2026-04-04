namespace Backend.Veteriner.Application.Common.Time;

/// <summary>
/// İş günü sınırları <see cref="OperationDayBounds"/> ile aynı İstanbul takvimine göre hafta ve ay aralıkları (UTC).
/// Hafta: Pazartesi 00:00–Pazartesi 00:00 (exclusive bitiş). Ay: ayın 1’i 00:00–sonraki ayın 1’i 00:00 (exclusive bitiş).
/// </summary>
public static class OperationPeriodBounds
{
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    public static (DateTime StartUtcInclusive, DateTime EndUtcExclusive) WeekForUtcNow(DateTime utcNow)
    {
        var normalizedUtc = NormalizeUtc(utcNow);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, IstanbulTimeZone);
        var daysFromMonday = ((int)localNow.DayOfWeek + 6) % 7;
        var weekStartLocal = localNow.Date.AddDays(-daysFromMonday);
        var weekEndLocal = weekStartLocal.AddDays(7);
        var weekStartUtc = TimeZoneInfo.ConvertTimeToUtc(weekStartLocal, IstanbulTimeZone);
        var weekEndUtc = TimeZoneInfo.ConvertTimeToUtc(weekEndLocal, IstanbulTimeZone);
        return (weekStartUtc, weekEndUtc);
    }

    public static (DateTime StartUtcInclusive, DateTime EndUtcExclusive) MonthForUtcNow(DateTime utcNow)
    {
        var normalizedUtc = NormalizeUtc(utcNow);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, IstanbulTimeZone);
        var monthStartLocal = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var monthEndLocal = monthStartLocal.AddMonths(1);
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, IstanbulTimeZone);
        var monthEndUtc = TimeZoneInfo.ConvertTimeToUtc(monthEndLocal, IstanbulTimeZone);
        return (monthStartUtc, monthEndUtc);
    }

    private static DateTime NormalizeUtc(DateTime utcNow)
        => utcNow.Kind switch
        {
            DateTimeKind.Utc => utcNow,
            DateTimeKind.Local => utcNow.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
        };

    private static TimeZoneInfo ResolveIstanbulTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }
}
