using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Vaccinations.Calendar;

/// <summary>Takvimdeki aşı kaydının kaynağı (APPOINTMENT_CALENDAR_API_CONTRACT.md §5).</summary>
public enum VaccinationCalendarKind
{
    /// <summary>Planlı aşı kaydı (<c>Scheduled</c>); tarih <c>DueAtUtc</c>.</summary>
    Planned = 0,

    /// <summary>Uygulanmış aşının sonraki uygulama tarihi (<c>Applied</c> + <c>DueAtUtc</c>; E-vet aktarması dahil).</summary>
    NextDue = 1,

    /// <summary>O gün uygulanmış aşı; tarih <c>AppliedAtUtc</c>.</summary>
    Applied = 2,
}

/// <summary>
/// Takvim aşı katmanının kuralları (tek yer). Durum (renk) <see cref="CalendarItemStateRule"/>: uygulanan aşı tamamlanan, vadeli aşı
/// vadesine göre bugün, gelecek ya da gecikmiş. Vadesi gecikmiş penceresinden (<see cref="VaccinationDueRule.OverdueWindow(DateTime)"/>,
/// son 1 takvim ayı) eski vadeli satır gösterilmez (<see cref="IsShown"/>); karşılanmış sonraki tarih
/// (<see cref="VaccinationDueRule.IsNextDueFulfilled"/>) de gösterilmez. Aşı randevu değildir: saat dilimini doldurmaz, çakışma ve boş
/// saat hesabına girmez, randevu hatırlatması üretmez.
/// </summary>
public static class VaccinationCalendarRules
{
    /// <summary>Satır takvimde görünür mü: uygulanan aşı her zaman; vadeli satır (planlı, sonraki tarih) vadesi pencereden eski değilse.</summary>
    public static bool IsShown(VaccinationCalendarKind kind, DateTime atUtc, VaccinationOverdueWindow overdueWindow)
        => kind == VaccinationCalendarKind.Applied || !VaccinationDueRule.IsBeforeOverdueWindow(atUtc, overdueWindow);

    public static CalendarItemState State(VaccinationCalendarKind kind, DateTime atUtc, DateTime utcNow)
        => kind == VaccinationCalendarKind.Applied ? CalendarItemState.Completed : CalendarItemStateRule.ForDue(atUtc, utcNow);

    /// <summary>
    /// Satırın saati gerçek mi (§8.2): vadeli satırda kaydın <c>DueTimeSpecified</c> değeri; uygulanan aşılar takvimde tek satıra
    /// toplanır, saati gösterilmez.
    /// </summary>
    public static bool TimeSpecified(VaccinationCalendarKind kind, bool dueTimeSpecified)
        => kind != VaccinationCalendarKind.Applied && dueTimeSpecified;

    /// <summary>Kaydın takvimde göründüğü İstanbul günü.</summary>
    public static DateOnly LocalDate(DateTime utc) => OperationDayBounds.ToLocalDate(utc);
}
