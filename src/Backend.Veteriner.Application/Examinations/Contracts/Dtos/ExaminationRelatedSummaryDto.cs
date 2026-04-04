using Backend.Veteriner.Domain.Payments;

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
    DateTime ResultDateUtc,
    Guid ClinicId,
    string ClinicName,
    string TestName);

public sealed record ExaminationRelatedHospitalizationItemDto(
    Guid Id,
    DateTime AdmittedAtUtc,
    Guid ClinicId,
    string ClinicName,
    string Reason,
    DateTime? DischargedAtUtc,
    bool IsActive);

public sealed record ExaminationRelatedPaymentItemDto(
    Guid Id,
    DateTime PaidAtUtc,
    Guid ClinicId,
    string ClinicName,
    decimal Amount,
    string Currency,
    PaymentMethod Method);
