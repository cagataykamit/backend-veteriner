using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

public sealed record PetHistorySummaryDto(
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    IReadOnlyList<PetHistoryAppointmentItemDto> RecentAppointments,
    IReadOnlyList<PetHistoryExaminationItemDto> RecentExaminations,
    IReadOnlyList<PetHistoryTreatmentItemDto> RecentTreatments,
    IReadOnlyList<PetHistoryPrescriptionItemDto> RecentPrescriptions,
    IReadOnlyList<PetHistoryLabResultItemDto> RecentLabResults,
    IReadOnlyList<PetHistoryHospitalizationItemDto> RecentHospitalizations,
    IReadOnlyList<PetHistoryPaymentItemDto> RecentPayments);

public sealed record PetHistoryAppointmentItemDto(
    Guid Id,
    DateTime ScheduledAtUtc,
    Guid ClinicId,
    string ClinicName,
    AppointmentStatus Status,
    AppointmentType AppointmentType,
    string? Notes);

public sealed record PetHistoryExaminationItemDto(
    Guid Id,
    DateTime ExaminedAtUtc,
    Guid ClinicId,
    string ClinicName,
    string VisitReason);

public sealed record PetHistoryTreatmentItemDto(
    Guid Id,
    DateTime TreatmentDateUtc,
    Guid ClinicId,
    string ClinicName,
    string Title,
    Guid? ExaminationId);

public sealed record PetHistoryPrescriptionItemDto(
    Guid Id,
    DateTime PrescribedAtUtc,
    Guid ClinicId,
    string ClinicName,
    string Title,
    Guid? ExaminationId,
    Guid? TreatmentId);

public sealed record PetHistoryLabResultItemDto(
    Guid Id,
    DateTime ResultDateUtc,
    Guid ClinicId,
    string ClinicName,
    string TestName,
    Guid? ExaminationId);

public sealed record PetHistoryHospitalizationItemDto(
    Guid Id,
    DateTime AdmittedAtUtc,
    Guid ClinicId,
    string ClinicName,
    string Reason,
    DateTime? DischargedAtUtc,
    bool IsActive);

public sealed record PetHistoryPaymentItemDto(
    Guid Id,
    DateTime PaidAtUtc,
    Guid ClinicId,
    string ClinicName,
    decimal Amount,
    string Currency,
    PaymentMethod Method);
