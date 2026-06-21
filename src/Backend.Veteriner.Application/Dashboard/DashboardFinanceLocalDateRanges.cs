using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Dashboard;

/// <summary>
/// Dashboard finance Query DB okuması için İstanbul takvim günü aralıkları.
/// <see cref="OperationPeriodBounds"/> / <see cref="OperationDayBounds"/> ile uyumludur.
/// </summary>
internal static class DashboardFinanceLocalDateRanges
{
    public static DateOnly TodayLocalDate(DateTime utcNow)
        => OperationDayBounds.ToLocalDate(utcNow);

    public static IReadOnlyList<DateOnly> WeekLocalDatesInclusive(DateTime utcNow)
    {
        var (weekStartUtc, _) = OperationPeriodBounds.WeekForUtcNow(utcNow);
        var weekStartLocal = OperationDayBounds.ToLocalDate(weekStartUtc);
        return Enumerable.Range(0, 7).Select(i => weekStartLocal.AddDays(i)).ToArray();
    }

    public static IReadOnlyList<DateOnly> MonthLocalDatesInclusive(DateTime utcNow)
    {
        var localDate = TodayLocalDate(utcNow);
        var monthStart = new DateOnly(localDate.Year, localDate.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(localDate.Year, localDate.Month);
        return Enumerable.Range(0, daysInMonth).Select(i => monthStart.AddDays(i)).ToArray();
    }
}
