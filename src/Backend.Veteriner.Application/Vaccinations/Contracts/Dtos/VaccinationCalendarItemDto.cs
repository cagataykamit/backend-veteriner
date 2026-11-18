using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Vaccinations.Calendar;

namespace Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;

/// <summary>
/// Randevu takviminde aşı katmanı satırı (APPOINTMENT_CALENDAR_API_CONTRACT.md §5). Aynı aşı kaydı iki satır verebilir: uygulandığı gün
/// (<c>Applied</c>) ve sonraki uygulama tarihi (<c>NextDue</c>); satır anahtarı <c>vaccinationId</c> + <c>kind</c>.
/// </summary>
/// <param name="Date">Satırın göründüğü İstanbul günü.</param>
/// <param name="AtUtc">Vade (<c>DueAtUtc</c>) ya da uygulama anı (<c>AppliedAtUtc</c>); saati yalnız <paramref name="TimeSpecified"/> ise gerçektir.</param>
/// <param name="TimeSpecified">Saat gerçek mi (§8.2): vadeli satırda kaydın <c>DueTimeSpecified</c> değeri, uygulanan satırda false.</param>
public sealed record VaccinationCalendarItemDto(
    Guid VaccinationId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VaccinationCalendarKind Kind,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CalendarItemState State,
    DateOnly Date,
    DateTime AtUtc,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? VaccineDefinitionId,
    string VaccineName,
    string? Notes,
    bool TimeSpecified = false);
