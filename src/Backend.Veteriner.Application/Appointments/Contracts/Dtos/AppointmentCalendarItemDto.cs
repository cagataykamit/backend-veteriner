using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

/// <param name="VeterinarianUserId">Randevunun hekimi; hekimsiz randevuda boş (takvimde "Hekim seçilmedi" sütunu).</param>
/// <param name="VeterinarianName">Hekimin görünen adı; ad bulunamazsa boş.</param>
/// <param name="ReasonId">Randevu nedeni; nedensiz randevuda boş.</param>
/// <param name="ReasonName">Nedenin adı (takvim ayrıntı balonu); bulunamazsa boş.</param>
/// <param name="Notes">Randevu notu (takvim ayrıntı balonu).</param>
public sealed record AppointmentCalendarItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid ClientId,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    DateTime ScheduledEndUtc,
    AppointmentStatus Status,
    AppointmentType AppointmentType,
    string PetName,
    string ClientName,
    Guid? VeterinarianUserId = null,
    string? VeterinarianName = null,
    Guid? ReasonId = null,
    string? ReasonName = null,
    string? Notes = null);
