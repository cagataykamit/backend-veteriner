using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Contracts.Dtos;

/// <summary>
/// Ödeme göstergesi yalnızca alınan tahsilatın varlığını söyler; borç/bakiye bilgisi yoktur (temel finans sonrası).
/// Bakım durumunu hiçbir koşulda etkilemez.
/// </summary>
public enum TodayPaymentIndicator
{
    NoPaymentRecorded = 0,
    PaymentRecorded = 1
}

/// <summary>
/// Bugün satırı: tek hasta gelişi veya (Visit'i olmayan) planlı randevu. Randevulu Visit tek satırdır.
/// Planlı satırda <see cref="VisitId"/> ve <see cref="CareStatus"/> null'dır.
/// <see cref="IsVoided"/> yalnızca yanlış işaretlenenler görünümünde (<c>voided=true</c>) doludur.
/// </summary>
public sealed record TodayItemDto(
    Guid? VisitId,
    Guid? AppointmentId,
    Guid PetId,
    string PetName,
    string? SpeciesName,
    Guid ClientId,
    string ClientName,
    string? ClientPhone,
    DateTime? ScheduledAtUtc,
    DateTime? ArrivedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VisitCareStatus? CareStatus,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentStatus? AppointmentStatus,
    Guid? ResponsibleVeterinarianUserId,
    string? ResponsibleVeterinarianName,
    bool IsCarriedOver,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TodayPaymentIndicator PaymentIndicator,
    bool HasActiveHospitalization,
    bool IsVoided,
    string? VoidReason,
    bool IsUrgent);

public sealed record TodayHospitalizationDto(
    Guid HospitalizationId,
    Guid PetId,
    string PetName,
    string ClientName,
    DateTime AdmittedAtUtc,
    DateTime? PlannedDischargeAtUtc);

public sealed record TodayDto(
    DateOnly Date,
    Guid ClinicId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<TodayItemDto> Items,
    IReadOnlyList<TodayHospitalizationDto> ActiveHospitalizations);
