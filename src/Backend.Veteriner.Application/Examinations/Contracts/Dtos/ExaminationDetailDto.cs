using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Contracts.Dtos;

/// <param name="AiProvenance">Muayeneye kabul edilmiş AI taslakları ("AI taslağından" izi, AI-010); yoksa boş.</param>
/// <param name="ClinicName">Muayenenin kliniğinin adı; klinik okunamazsa boş.</param>
public sealed record ExaminationDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    string VisitReason,
    string? Anamnesis,
    string Findings,
    decimal? WeightKg,
    decimal? TemperatureC,
    int? HeartRateBpm,
    int? RespiratoryRatePerMin,
    DateTime? VitalsMeasuredAtUtc,
    string? Assessment,
    string? Plan,
    string? Notes,
    string RowVersion,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid? VisitId = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ExaminationStatus Status = ExaminationStatus.Draft,
    DateTime? FinalizedAtUtc = null,
    Guid? FinalizedByUserId = null,
    string? FinalizedByName = null,
    IReadOnlyList<AiProvenanceDto>? AiProvenance = null,
    string ClinicName = "");
