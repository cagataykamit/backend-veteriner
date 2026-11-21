using System.Linq.Expressions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>
/// Planlı aşının "gecikmiş" ve "yaklaşan" tanımı (ADR-028): gün sınırı klinik yerel günüdür (Europe/Istanbul).
/// Gecikmiş = vadesi son <see cref="OverdueVisibleMonths"/> takvim ayı içinde ve bugünün başından önce (<see cref="OverdueWindow(DateOnly)"/>);
/// bugün vadesi olan aşı saati geçse de gecikmiş değildir. Pencereden eski vade hiçbir gecikmiş görünümünde (liste filtresi, sayaçlar,
/// takvim, hasta kartı) yer almaz; kayıt durumuyla (planlı) aşı listesinde kalır.
/// Yaklaşan N gün = vade <c>[bugünün başı, bugün + N gün başı)</c>, yani bugün dahil N takvim günü.
/// Tanım yalnızca burada yaşar; pano sayaçları, <c>onlyOverdue</c> liste filtresi, hasta özeti <c>isOverdue</c>, hasta
/// "Yapılacaklar", portal, takvim ve AI araması bunu kullanır. Vefat eden hastanın aşısı her iki tanımda sayılmaz; dışlama çağıran tarafta
/// <c>Pet.ReceivesCare</c> ile yapılır (ADR-020 S5).
/// </summary>
public static class VaccinationDueRule
{
    /// <summary>Pano "önümüzdeki 7 gün" uyarısının gün sayısı (bugün dahil).</summary>
    public const int DashboardUpcomingDays = 7;

    /// <summary>
    /// Gecikmiş aşının görünür kaldığı takvim ayı (2026-11-21 kararı): bugün T ise <c>T.AddMonths(-1) &lt;= vade günü &lt; T</c>.
    /// Sabit gün sayısı değildir; ay sonu kırpılır (31 Mart → 28/29 Şubat).
    /// </summary>
    public const int OverdueVisibleMonths = 1;

    /// <summary>İstanbul günü <paramref name="today"/> için gecikmiş penceresi: <c>[today − 1 ay başı, today başı)</c> (UTC).</summary>
    public static VaccinationOverdueWindow OverdueWindow(DateOnly today)
        => new(
            OperationDayBounds.ForLocalDate(today.AddMonths(-OverdueVisibleMonths)).DayStartUtc,
            OperationDayBounds.ForLocalDate(today).DayStartUtc);

    /// <summary><paramref name="utcNow"/> anındaki İstanbul gününe göre gecikmiş penceresi.</summary>
    public static VaccinationOverdueWindow OverdueWindow(DateTime utcNow) => OverdueWindow(OperationDayBounds.ToLocalDate(utcNow));

    /// <summary>Bugün dahil <paramref name="days"/> İstanbul takvim günü: <c>[bugünün başı, bugün + days gün başı)</c> (UTC).</summary>
    public static (DateTime FromUtc, DateTime ToUtcExclusive) UpcomingWindow(DateTime utcNow, int days)
    {
        var today = OperationDayBounds.ToLocalDate(utcNow);
        return (OperationDayBounds.ForLocalDate(today).DayStartUtc, OperationDayBounds.ForLocalDate(today.AddDays(days)).DayStartUtc);
    }

    public static Expression<Func<Vaccination, bool>> IsOverdue(VaccinationOverdueWindow window)
    {
        var fromUtc = window.FromUtc;
        var beforeUtc = window.BeforeUtc;
        return v => v.Status == VaccinationStatus.Scheduled && v.DueAtUtc != null && v.DueAtUtc >= fromUtc && v.DueAtUtc < beforeUtc;
    }

    public static Expression<Func<Vaccination, bool>> IsUpcoming(DateTime fromUtc, DateTime toUtcExclusive)
        => v => v.Status == VaccinationStatus.Scheduled && v.DueAtUtc != null && v.DueAtUtc >= fromUtc && v.DueAtUtc < toUtcExclusive;

    /// <summary>Bellekteki planlı aşı vadesi için <see cref="IsOverdue(VaccinationOverdueWindow)"/> ile aynı karşılaştırma.</summary>
    public static bool IsOverdueAt(DateTime dueAtUtc, VaccinationOverdueWindow window)
        => dueAtUtc >= window.FromUtc && dueAtUtc < window.BeforeUtc;

    /// <summary>Vade pencereden eski: gecikmiş de yaklaşan da sayılmaz; takvimde, hasta kartında ve portalda gösterilmez.</summary>
    public static bool IsBeforeOverdueWindow(DateTime dueAtUtc, VaccinationOverdueWindow window) => dueAtUtc < window.FromUtc;
}

/// <summary>Gecikmiş aşı penceresi (UTC, yarı açık): <c>[FromUtc, BeforeUtc)</c>; bkz. <see cref="VaccinationDueRule.OverdueWindow(DateOnly)"/>.</summary>
public readonly record struct VaccinationOverdueWindow(DateTime FromUtc, DateTime BeforeUtc);
