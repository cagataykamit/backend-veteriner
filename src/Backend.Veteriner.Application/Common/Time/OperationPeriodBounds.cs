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

    /// <summary>
    /// Son 7 takvim gününü Europe/Istanbul bazında döner (bugün dahil, en eskiden en yeniye sıralı).
    /// Her gün için İstanbul yerel günü (<see cref="DateOnly"/>) ve UTC [start, end) sınırları sağlanır; bu sayede
    /// DST geçişlerinde gün uzunlukları farklı olsa bile her bucket doğru pencereye eşlenir.
    /// Döndürülen listede 7 eleman garanti edilir; çağıran taraf bucket'ları sıralı olarak iterate edebilir.
    /// </summary>
    public static IReadOnlyList<DailyWindow> Last7DaysForUtcNow(DateTime utcNow)
    {
        var normalizedUtc = NormalizeUtc(utcNow);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, IstanbulTimeZone);
        var todayLocal = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);

        var result = new List<DailyWindow>(7);
        for (var i = 6; i >= 0; i--)
        {
            var dayStartLocal = todayLocal.AddDays(-i);
            var dayEndLocal = dayStartLocal.AddDays(1);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, IstanbulTimeZone);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, IstanbulTimeZone);
            result.Add(new DailyWindow(DateOnly.FromDateTime(dayStartLocal), startUtc, endUtc));
        }

        return result;
    }

    /// <summary>
    /// Günlük pencere tanımı: İstanbul takvim günü (<see cref="LocalDate"/>) ve ona karşılık gelen UTC yarı-açık aralık
    /// (<see cref="StartUtcInclusive"/>..<see cref="EndUtcExclusive"/>).
    /// </summary>
    public readonly record struct DailyWindow(DateOnly LocalDate, DateTime StartUtcInclusive, DateTime EndUtcExclusive);

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
