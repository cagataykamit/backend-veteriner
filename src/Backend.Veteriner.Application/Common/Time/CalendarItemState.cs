using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Common.Time;

/// <summary>
/// Takvim kaydının durumu (renk açıklaması ve "Durum" filtresi; APPOINTMENT_CALENDAR_API_CONTRACT.md §5–6). Randevu ve aşı aynı dili kullanır:
/// bugün, gelecek, gecikmiş, tamamlanan.
/// </summary>
public enum CalendarItemState
{
    DueToday = 0,
    Upcoming = 1,
    Overdue = 2,
    Completed = 3,
}

/// <summary>
/// Durumun tek yeri. Gün sınırı ve gecikme tanımı <see cref="VaccinationDueRule"/> (ADR-028, İstanbul günü): bugünün başından önceki vade
/// gecikmiş, bugün içindeki vade saati geçse de "bugün".
/// </summary>
public static class CalendarItemStateRule
{
    /// <summary>Yapılmamış (planlı) kaydın vadesine göre durumu.</summary>
    public static CalendarItemState ForDue(DateTime dueAtUtc, DateTime utcNow)
    {
        var (todayStartUtc, tomorrowStartUtc) = VaccinationDueRule.UpcomingWindow(utcNow, 1);
        if (VaccinationDueRule.IsOverdueAt(dueAtUtc, todayStartUtc))
            return CalendarItemState.Overdue;

        return dueAtUtc < tomorrowStartUtc ? CalendarItemState.DueToday : CalendarItemState.Upcoming;
    }

    /// <summary>
    /// Randevunun durumu: tamamlanan randevu tamamlanan; planlı randevu gününe göre bugün, gelecek ya da gecikmiş. İptal ve gelmedi hiçbir
    /// duruma girmez (durum filtresi seçiliyken görünmez).
    /// </summary>
    public static CalendarItemState? ForAppointment(AppointmentStatus status, DateTime scheduledAtUtc, DateTime utcNow)
        => status switch
        {
            AppointmentStatus.Completed => CalendarItemState.Completed,
            AppointmentStatus.Scheduled => ForDue(scheduledAtUtc, utcNow),
            _ => null,
        };
}
