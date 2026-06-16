namespace Backend.Veteriner.Application.Common.Time;

public static class OperationDayBounds
{
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    public static (DateTime DayStartUtc, DateTime DayEndUtc) ForUtcNow(DateTime utcNow)
    {
        var normalizedUtc = NormalizeUtc(utcNow);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, IstanbulTimeZone);
        var localDayStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localDayEnd = localDayStart.AddDays(1);

        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localDayStart, IstanbulTimeZone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(localDayEnd, IstanbulTimeZone);
        return (dayStartUtc, dayEndUtc);
    }

    /// <summary>
    /// İstanbul takvim günü için UTC yarı-açık aralık [DayStartUtc, DayEndUtc).
    /// </summary>
    public static (DateTime DayStartUtc, DateTime DayEndUtc) ForLocalDate(DateOnly localDate)
    {
        var localDayStart = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localDayEnd = localDayStart.AddDays(1);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localDayStart, IstanbulTimeZone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(localDayEnd, IstanbulTimeZone);
        return (dayStartUtc, dayEndUtc);
    }

    /// <summary>
    /// UTC anını İstanbul takvim gününe çevirir.
    /// </summary>
    public static DateOnly ToLocalDate(DateTime scheduledAtUtc)
    {
        var normalizedUtc = NormalizeUtc(scheduledAtUtc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, IstanbulTimeZone);
        return DateOnly.FromDateTime(local);
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
