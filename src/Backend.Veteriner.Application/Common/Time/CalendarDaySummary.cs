namespace Backend.Veteriner.Application.Common.Time;

/// <summary>
/// Takvim ay görünümünde bir günün özeti (APPOINTMENT_CALENDAR_API_CONTRACT.md §7): toplam ve durumlara göre sayılar. Durumu olmayan
/// kayıt (iptal, gelmedi) sayılmaz.
/// </summary>
public sealed record CalendarDaySummaryDto(DateOnly Date, int Total, int DueToday, int Upcoming, int Overdue, int Completed);

public static class CalendarDaySummary
{
    /// <summary>Gün başına sayılar; yalnız kaydı olan günler, tarih sırasıyla.</summary>
    public static IReadOnlyList<CalendarDaySummaryDto> From(IEnumerable<(DateOnly Date, CalendarItemState? State)> items)
        => items
            .Where(i => i.State is not null)
            .GroupBy(i => i.Date)
            .OrderBy(g => g.Key)
            .Select(g => new CalendarDaySummaryDto(
                g.Key,
                g.Count(),
                g.Count(i => i.State == CalendarItemState.DueToday),
                g.Count(i => i.State == CalendarItemState.Upcoming),
                g.Count(i => i.State == CalendarItemState.Overdue),
                g.Count(i => i.State == CalendarItemState.Completed)))
            .ToList();
}
