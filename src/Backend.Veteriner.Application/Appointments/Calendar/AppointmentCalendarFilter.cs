using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Calendar;

/// <summary>
/// Takvim filtreleri (APPOINTMENT_CALENDAR_API_CONTRACT.md §6): hekim (ya da yalnız hekimsiz), randevu türleri ve durum. Boş filtre her
/// randevuyu geçirir; dolu filtreler birlikte (VE) uygulanır. Takvim aralığı en çok 45 gün olduğundan sunucuda bellekte uygulanır; komut
/// ve okuma modeli yolları aynı kuralı kullanır.
/// </summary>
public sealed record AppointmentCalendarFilter(
    Guid? VeterinarianUserId,
    bool WithoutVeterinarian,
    IReadOnlyCollection<AppointmentType>? AppointmentTypes,
    IReadOnlyCollection<CalendarItemState>? States)
{
    public bool IsEmpty => VeterinarianUserId is null && !WithoutVeterinarian && AppointmentTypes is not { Count: > 0 } && States is not { Count: > 0 };

    public bool Matches(Guid? veterinarianUserId, AppointmentType appointmentType, AppointmentStatus status, DateTime scheduledAtUtc, DateTime utcNow)
    {
        if (VeterinarianUserId is { } vet && veterinarianUserId != vet)
            return false;
        if (WithoutVeterinarian && veterinarianUserId is not null)
            return false;
        if (AppointmentTypes is { Count: > 0 } types && !types.Contains(appointmentType))
            return false;

        return States is not { Count: > 0 } states
               || CalendarItemStateRule.ForAppointment(status, scheduledAtUtc, utcNow) is { } state && states.Contains(state);
    }
}
