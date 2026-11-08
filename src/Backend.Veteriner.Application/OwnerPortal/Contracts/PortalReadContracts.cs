using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.OwnerPortal.Contracts;

// Hasta sahibi portalı dilim B salt okunur yanıtları (docs/PORTAL_API_CONTRACT.md §6). Personel DTO'ları yeniden kullanılmaz;
// her alan burada açıkça seçilir. Kiracı, sürüm, kullanıcı kimliği, not ve klinik içerik alanı bilerek yoktur.

public sealed record PortalClinicWorkingHourDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] DayOfWeek DayOfWeek,
    bool IsClosed,
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt,
    TimeOnly? BreakStartsAt,
    TimeOnly? BreakEndsAt);

public sealed record PortalClinicDto(
    Guid ClinicId,
    string Name,
    string City,
    string? Phone,
    string? Email,
    string? Address,
    IReadOnlyList<PortalClinicWorkingHourDto> WorkingHours);

public sealed record PortalPetDto(
    Guid PetId,
    string Name,
    string? SpeciesName,
    string? BreedName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetGender? Gender,
    DateOnly? BirthDate,
    string? MicrochipNumber,
    decimal? WeightKg,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status);

public sealed record PortalPetDetailDto(
    Guid PetId,
    string Name,
    string? SpeciesName,
    string? BreedName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetGender? Gender,
    DateOnly? BirthDate,
    string? MicrochipNumber,
    decimal? WeightKg,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status,
    bool IsNeutered,
    string? ColorName);

public sealed record PortalVaccinationHistoryItemDto(
    Guid VaccinationId,
    string VaccineName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc);

public sealed record PortalUpcomingVaccinationDto(
    Guid VaccinationId,
    string VaccineName,
    DateTime? DueAtUtc,
    bool IsOverdue);

public sealed record PortalPetVaccinationsDto(
    IReadOnlyList<PortalVaccinationHistoryItemDto> History,
    IReadOnlyList<PortalUpcomingVaccinationDto> Upcoming);

/// <summary>
/// Randevu satırı. <see cref="GoogleCalendarUrl"/> (TIER-020) yalnız takvime eklenebilen randevuda (planlı ve saati gelmemiş)
/// doludur; .ics aynı koşulla <c>GET appointments/{id}/calendar.ics</c>'ten alınır.
/// </summary>
public sealed record PortalAppointmentDto(
    Guid AppointmentId,
    Guid PetId,
    string PetName,
    Guid ClinicId,
    string? ClinicName,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentType AppointmentType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentStatus Status,
    string? GoogleCalendarUrl = null);

/// <summary>Bakiye: yalnız tutar/durum (ADR-031 AK-9), bağın kliniğiyle sınırlı.</summary>
public sealed record PortalBalanceDto(
    Guid ClinicId,
    string Currency,
    decimal TotalCharged,
    decimal TotalPaid,
    decimal Outstanding,
    decimal UnallocatedPaymentsAmount,
    DateTime? LastPaymentAtUtc,
    decimal CreditBalance = 0m);

/// <summary>Ödeme geçmişi satırı: yalnız tarih, tutar, yöntem.</summary>
public sealed record PortalPaymentDto(
    DateTime PaidAtUtc,
    decimal Amount,
    string Currency,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentMethod Method);
