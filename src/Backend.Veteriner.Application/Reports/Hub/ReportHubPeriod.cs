using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Reports.Hub;

/// <summary>İstanbul saatiyle yarı açık pencere <c>[FromUtc, ToUtcExclusive)</c> ve kapsadığı takvim günleri (ikisi dahil).</summary>
public sealed record ReportHubWindow(DateTime FromUtc, DateTime ToUtcExclusive, DateOnly From, DateOnly To);

/// <summary>
/// Rapor Merkezi ana ekranının dönem pencereleri (tek yer; REPORT_HUB_SUMMARY_API_CONTRACT.md §2). Bu dönem başından şimdiye kadardır;
/// karşılaştırma dönemi aynı geçen süreyi kapsar (dün aynı saate, geçen hafta aynı ana, geçen ay aynı güne kadar; geçen ay kısaysa ay sonu).
/// Seri noktaları: bugün için son 7 gün, hafta için Pazartesiden bugüne günler, ay için ayın Pazartesi başlayan haftaları.
/// </summary>
public sealed record ReportHubPeriod(
    string Name,
    ReportHubWindow Current,
    ReportHubWindow Previous,
    IReadOnlyList<ReportHubWindow> Series)
{
    public const string Today = "today";
    public const string Week = "week";
    public const string Month = "month";

    private const int TodaySeriesDays = 7;

    public static bool IsKnown(string? name) => name is Today or Week or Month;

    /// <summary>Çağıran adı önce <see cref="IsKnown"/> ile doğrular.</summary>
    public static ReportHubPeriod For(string name, DateTime nowUtc)
    {
        var today = OperationDayBounds.ToLocalDate(nowUtc);
        var (start, previousStart) = name switch
        {
            Today => (today, today.AddDays(-1)),
            Week => (MondayOf(today), MondayOf(today).AddDays(-7)),
            Month => (new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(-1)),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Tanımsız dönem."),
        };

        var current = Window(StartUtc(start), nowUtc);
        var previousFromUtc = StartUtc(previousStart);
        var previousToUtc = previousFromUtc + (nowUtc - current.FromUtc);
        if (previousToUtc > current.FromUtc)
            previousToUtc = current.FromUtc;

        return new ReportHubPeriod(name, current, Window(previousFromUtc, previousToUtc), SeriesFor(name, start, today, nowUtc));
    }

    private static IReadOnlyList<ReportHubWindow> SeriesFor(string name, DateOnly start, DateOnly today, DateTime nowUtc)
    {
        if (name == Today)
            return Days(today.AddDays(-(TodaySeriesDays - 1)), today, nowUtc);
        if (name == Week)
            return Days(start, today, nowUtc);

        var weeks = new List<ReportHubWindow>();
        for (var cursor = start; cursor <= today; cursor = MondayOf(cursor).AddDays(7))
        {
            var nextMonday = MondayOf(cursor).AddDays(7);
            weeks.Add(Window(StartUtc(cursor), nextMonday > today ? nowUtc : StartUtc(nextMonday)));
        }

        return weeks;
    }

    private static List<ReportHubWindow> Days(DateOnly first, DateOnly today, DateTime nowUtc)
    {
        var days = new List<ReportHubWindow>();
        for (var day = first; day <= today; day = day.AddDays(1))
            days.Add(Window(StartUtc(day), day == today ? nowUtc : StartUtc(day.AddDays(1))));
        return days;
    }

    /// <summary>Boş pencerede (şimdi tam gün başı) son gün ilk gündür.</summary>
    private static ReportHubWindow Window(DateTime fromUtc, DateTime toUtcExclusive)
    {
        var from = OperationDayBounds.ToLocalDate(fromUtc);
        var to = toUtcExclusive > fromUtc ? OperationDayBounds.ToLocalDate(toUtcExclusive.AddTicks(-1)) : from;
        return new ReportHubWindow(fromUtc, toUtcExclusive, from, to);
    }

    private static DateTime StartUtc(DateOnly day) => OperationDayBounds.ForLocalDate(day).DayStartUtc;

    private static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
