using System.Linq.Expressions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>
/// Planlı aşının "gecikmiş" ve "yaklaşan" tanımı (ADR-028): gün sınırı klinik yerel günüdür (Europe/Istanbul).
/// Gecikmiş = vadesi bugünün başından önce; bugün vadesi olan aşı saati geçse de gecikmiş değildir.
/// Yaklaşan N gün = vade <c>[bugünün başı, bugün + N gün başı)</c>, yani bugün dahil N takvim günü.
/// Tanım yalnızca burada yaşar; pano sayaçları, <c>onlyOverdue</c> liste süzgeci, hasta özeti <c>isOverdue</c> ve hasta
/// "Yapılacaklar" bunu kullanır. Vefat eden hastanın aşısı her iki tanımda sayılmaz; dışlama çağıran tarafta
/// <c>Pet.ReceivesCare</c> ile yapılır (ADR-020 S5).
/// </summary>
public static class VaccinationDueRule
{
    /// <summary>Pano "önümüzdeki 7 gün" uyarısının gün sayısı (bugün dahil).</summary>
    public const int DashboardUpcomingDays = 7;

    /// <summary>Gecikme sınırı: <paramref name="utcNow"/> anındaki İstanbul gününün başlangıcı (UTC); vade bundan önceyse gecikmiş.</summary>
    public static DateTime OverdueBeforeUtc(DateTime utcNow) => OperationDayBounds.ForUtcNow(utcNow).DayStartUtc;

    /// <summary>Bugün dahil <paramref name="days"/> İstanbul takvim günü: <c>[bugünün başı, bugün + days gün başı)</c> (UTC).</summary>
    public static (DateTime FromUtc, DateTime ToUtcExclusive) UpcomingWindow(DateTime utcNow, int days)
    {
        var today = OperationDayBounds.ToLocalDate(utcNow);
        return (OperationDayBounds.ForLocalDate(today).DayStartUtc, OperationDayBounds.ForLocalDate(today.AddDays(days)).DayStartUtc);
    }

    public static Expression<Func<Vaccination, bool>> IsOverdue(DateTime overdueBeforeUtc)
        => v => v.Status == VaccinationStatus.Scheduled && v.DueAtUtc != null && v.DueAtUtc < overdueBeforeUtc;

    public static Expression<Func<Vaccination, bool>> IsUpcoming(DateTime fromUtc, DateTime toUtcExclusive)
        => v => v.Status == VaccinationStatus.Scheduled && v.DueAtUtc != null && v.DueAtUtc >= fromUtc && v.DueAtUtc < toUtcExclusive;

    /// <summary>Bellekteki planlı aşı vadesi için <see cref="IsOverdue(DateTime)"/> ile aynı karşılaştırma.</summary>
    public static bool IsOverdueAt(DateTime dueAtUtc, DateTime overdueBeforeUtc) => dueAtUtc < overdueBeforeUtc;
}
