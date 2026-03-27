namespace Backend.Veteriner.Application.Common.Time;

public static class OperationDayBounds
{
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    public static (DateTime DayStartUtc, DateTime DayEndUtc) ForUtcNow(DateTime utcNow)
    {
        var normalizedUtc = utcNow.Kind switch
        {
            DateTimeKind.Utc => utcNow,
            DateTimeKind.Local => utcNow.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
        };

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, IstanbulTimeZone);
        var localDayStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localDayEnd = localDayStart.AddDays(1);

        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localDayStart, IstanbulTimeZone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(localDayEnd, IstanbulTimeZone);
        return (dayStartUtc, dayEndUtc);
    }

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
