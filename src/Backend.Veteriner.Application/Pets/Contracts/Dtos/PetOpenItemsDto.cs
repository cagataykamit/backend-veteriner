using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

public sealed record PetOpenItemsVaccinationDto(
    Guid Id,
    string VaccineName,
    DateTime DueAtUtc,
    Guid ClinicId);

/// <summary>Gecikmiş (vade &lt; bugünün İstanbul başı) ve yaklaşan (bugün dahil 30 takvim günü) planlı aşılar; ikisi de en erken vade önce (ADR-028).</summary>
public sealed record PetOpenItemsVaccinationsDto(
    IReadOnlyList<PetOpenItemsVaccinationDto> Overdue,
    IReadOnlyList<PetOpenItemsVaccinationDto> Upcoming);

public sealed record PetOpenItemsLabResultDto(
    Guid Id,
    string TestName,
    DateTime RequestedAtUtc,
    DateTime? ResultDateUtc,
    Guid? ExaminationId,
    Guid? VisitId,
    Guid ClinicId);

/// <summary>Sonucu bekleyen (<c>Requested</c>) ve yorum bekleyen lab kayıtları; sınırsız, en eski istek önce.</summary>
public sealed record PetOpenItemsLabResultsDto(
    IReadOnlyList<PetOpenItemsLabResultDto> AwaitingResult,
    IReadOnlyList<PetOpenItemsLabResultDto> AwaitingInterpretation);

/// <param name="TotalAmount">Fiyatlı kalemlerin <c>round(miktar × önerilen fiyat, 2)</c> toplamı (TRY).</param>
/// <param name="UnpricedCount">Fiyatı olmayan (resepsiyonun gireceği) kalem sayısı; tutara katılmaz.</param>
public sealed record PetOpenItemsPendingChargesDto(
    int Count,
    decimal TotalAmount,
    int UnpricedCount,
    string Currency);

public sealed record PetOpenItemsAppointmentDto(
    Guid Id,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentType AppointmentType,
    Guid ClinicId,
    string ClinicName);

/// <param name="PetOutstandingAmount">Hayvanın güncel sahibine yazılmış, bu hayvana ait ücretlerin açık toplamı.</param>
/// <param name="ClientOutstandingAmount">Sahibin aynı kapsamdaki toplam açık borcu (bakiye ucuyla aynı kural).</param>
public sealed record PetOpenItemsOutstandingDto(
    decimal PetOutstandingAmount,
    int PetOpenChargeCount,
    decimal ClientOutstandingAmount,
    string Currency);

/// <summary>
/// Hasta kartı "Yapılacaklar": her bölüm <see cref="PetSummarySectionDto{T}"/> durumu taşır
/// (<c>Ok</c> / <c>Empty</c> / <c>Forbidden</c> / <c>Unavailable</c>).
/// </summary>
public sealed record PetOpenItemsDto(
    Guid PetId,
    Guid ClientId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status,
    DateTime AsOfUtc,
    PetSummarySectionDto<PetOpenItemsVaccinationsDto> Vaccinations,
    PetSummarySectionDto<PetOpenItemsLabResultsDto> LabResults,
    PetSummarySectionDto<PetOpenItemsPendingChargesDto> PendingCharges,
    PetSummarySectionDto<PetOpenItemsAppointmentDto> NextAppointment,
    PetSummarySectionDto<PetOpenItemsOutstandingDto> Outstanding);
