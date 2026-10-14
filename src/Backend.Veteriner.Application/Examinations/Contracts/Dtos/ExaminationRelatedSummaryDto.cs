using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Examinations.Contracts.Dtos;

public sealed record ExaminationRelatedSummaryDto(
    Guid ExaminationId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    IReadOnlyList<ExaminationRelatedTreatmentItemDto> Treatments,
    IReadOnlyList<ExaminationRelatedPrescriptionItemDto> Prescriptions,
    IReadOnlyList<ExaminationRelatedLabResultItemDto> LabResults,
    IReadOnlyList<ExaminationRelatedHospitalizationItemDto> Hospitalizations,
    IReadOnlyList<ExaminationRelatedAdministrationItemDto> Administrations,
    IReadOnlyList<ExaminationRelatedVaccinationItemDto> Vaccinations,
    IReadOnlyList<ExaminationRelatedPaymentItemDto> Payments);

public sealed record ExaminationRelatedTreatmentItemDto(
    Guid Id,
    DateTime TreatmentDateUtc,
    Guid ClinicId,
    string ClinicName,
    string Title);

public sealed record ExaminationRelatedPrescriptionItemDto(
    Guid Id,
    DateTime PrescribedAtUtc,
    Guid ClinicId,
    string ClinicName,
    string Title,
    Guid? TreatmentId);

public sealed record ExaminationRelatedLabResultItemDto(
    Guid Id,
    DateTime? ResultDateUtc,
    Guid ClinicId,
    string ClinicName,
    string TestName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LabResultStatus Status,
    DateTime RequestedAtUtc,
    bool AwaitingInterpretation);

public sealed record ExaminationRelatedHospitalizationItemDto(
    Guid Id,
    DateTime AdmittedAtUtc,
    Guid ClinicId,
    string ClinicName,
    string Reason,
    DateTime? DischargedAtUtc,
    bool IsActive);

/// <summary>İptal edilmemiş uygulama; <see cref="AddedAfterFinalization"/> "kesinleştirmeden sonra eklendi" rozetidir.</summary>
public sealed record ExaminationRelatedAdministrationItemDto(
    Guid Id,
    DateTime AdministeredAtUtc,
    Guid ClinicId,
    string ClinicName,
    string ProductName,
    string Unit,
    decimal Quantity,
    bool AddedAfterFinalization,
    string? LateEntryReason);

/// <summary>Muayeneye bağlı aşı kaydı (her durum); tarihlerden dolu olan(lar) durumla tutarlıdır.</summary>
public sealed record ExaminationRelatedVaccinationItemDto(
    Guid Id,
    Guid ClinicId,
    string ClinicName,
    string VaccineName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc);

public sealed record ExaminationRelatedPaymentItemDto(
    Guid Id,
    DateTime PaidAtUtc,
    Guid ClinicId,
    string ClinicName,
    decimal Amount,
    string Currency,
    PaymentMethod Method);
