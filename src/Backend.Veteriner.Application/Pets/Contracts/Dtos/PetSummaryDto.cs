using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

/// <summary>
/// Bölüm durumu: <c>Ok</c> kayıt var; <c>Empty</c> kayıt yok; <c>Forbidden</c> ilgili okuma izni yok;
/// <c>Unavailable</c> bölüm sorgusu hata verdi (diğer bölümler yine döner).
/// </summary>
public enum PetSummarySectionState
{
    Ok = 0,
    Empty = 1,
    Forbidden = 2,
    Unavailable = 3
}

public sealed record PetSummarySectionDto<T>(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetSummarySectionState State,
    T? Data) where T : class
{
    public static PetSummarySectionDto<T> Ok(T data) => new(PetSummarySectionState.Ok, data);
    public static PetSummarySectionDto<T> Empty() => new(PetSummarySectionState.Empty, null);
    public static PetSummarySectionDto<T> Forbidden() => new(PetSummarySectionState.Forbidden, null);
    public static PetSummarySectionDto<T> Unavailable() => new(PetSummarySectionState.Unavailable, null);
}

public sealed record PetSummaryActiveHospitalizationDto(
    Guid Id,
    Guid ClinicId,
    string ClinicName,
    DateTime AdmittedAtUtc,
    DateTime? PlannedDischargeAtUtc,
    string Reason,
    Guid? ExaminationId);

public sealed record PetSummaryLastVaccinationDto(
    Guid Id,
    string VaccineName,
    DateTime AppliedAtUtc,
    Guid ClinicId);

public sealed record PetSummaryNextVaccinationDto(
    Guid Id,
    string VaccineName,
    DateTime DueAtUtc,
    bool IsOverdue,
    Guid ClinicId);

/// <summary>Son uygulanan ve en yakın planlı aşı ayrı alanlardır; biri yoksa null.</summary>
public sealed record PetSummaryVaccinationsDto(
    PetSummaryLastVaccinationDto? LastApplied,
    PetSummaryNextVaccinationDto? NextDue);

/// <summary>Muayene vitalinden türetilen tarihli kilo (<c>Source</c> bugün hep <c>Examination</c>).</summary>
public sealed record PetSummaryWeightDto(
    decimal WeightKg,
    DateTime MeasuredAtUtc,
    string Source,
    Guid ExaminationId);

public sealed record PetSummaryLastVisitDto(
    Guid Id,
    DateTime ArrivedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VisitCareStatus CareStatus,
    Guid ClinicId,
    string ClinicName);

public sealed record PetSummaryLastExaminationDto(
    Guid Id,
    DateTime ExaminedAtUtc,
    string VisitReason,
    Guid ClinicId,
    string ClinicName);

/// <param name="ProfileWeightKg">Hayvan profilindeki tarihsiz kilo (<c>Pet.Weight</c>).</param>
/// <param name="Status">Hayvan durumu (<c>Active</c> | <c>Deceased</c>); vefat edende <c>nextDue.isOverdue</c> hep false.</param>
public sealed record PetSummaryDto(
    Guid PetId,
    PetAlertsDto PetAlerts,
    decimal? ProfileWeightKg,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status,
    PetSummarySectionDto<PetSummaryActiveHospitalizationDto> ActiveHospitalization,
    PetSummarySectionDto<PetSummaryVaccinationsDto> Vaccinations,
    PetSummarySectionDto<PetSummaryWeightDto> LatestWeight,
    PetSummarySectionDto<PetSummaryLastVisitDto> LastVisit,
    PetSummarySectionDto<PetSummaryLastExaminationDto> LastExamination);
