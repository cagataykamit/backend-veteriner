using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

/// <param name="VeterinarianUserId">Randevunun hekimi; hekimsiz randevuda boş (takvimde "Hekim seçilmedi" sütunu).</param>
/// <param name="VeterinarianName">Hekimin görünen adı; ad bulunamazsa boş.</param>
/// <param name="ReasonId">Randevu nedeni; nedensiz randevuda boş.</param>
/// <param name="ReasonName">Nedenin adı (takvim ayrıntı balonu); bulunamazsa boş.</param>
/// <param name="Notes">Randevu notu (takvim ayrıntı balonu).</param>
/// <param name="State">Takvim durumu (§6.1, §8.3; <see cref="CalendarItemStateRule.ForAppointment"/>); iptal ve gelmedi randevuda boş.</param>
/// <param name="RoomResourceId">Randevunun odası (ADR-053); yoksa boş. Kaynak sütunlu takvim görünümü bu alanla çizer.</param>
/// <param name="RoomResourceName">Odanın adı (pasif oda da adıyla); bulunamazsa boş.</param>
/// <param name="EquipmentResourceId">Randevunun cihazı; yoksa boş.</param>
/// <param name="EquipmentResourceName">Cihazın adı; bulunamazsa boş.</param>
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
    string? Notes = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CalendarItemState? State = null,
    Guid? RoomResourceId = null,
    string? RoomResourceName = null,
    Guid? EquipmentResourceId = null,
    string? EquipmentResourceName = null);
