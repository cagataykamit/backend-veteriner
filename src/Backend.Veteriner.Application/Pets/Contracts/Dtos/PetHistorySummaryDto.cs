using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

/// <param name="OmittedBlocks">Yetki yok nedeniyle boş bırakılan bloklar; boş blok + listede yok = kayıt yok.</param>
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
    IReadOnlyList<PetHistoryPaymentItemDto> RecentPayments,
    IReadOnlyList<string> OmittedBlocks);

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
    string VisitReason,
    Guid? VisitId,
    DateTime? UpdatedAtUtc);

public sealed record PetHistoryTreatmentItemDto(
    Guid Id,
    DateTime TreatmentDateUtc,
    Guid ClinicId,
    string ClinicName,
    string Title,
    Guid? ExaminationId,
    DateTime? UpdatedAtUtc);

public sealed record PetHistoryPrescriptionItemDto(
    Guid Id,
    DateTime PrescribedAtUtc,
    Guid ClinicId,
    string ClinicName,
    string Title,
    Guid? ExaminationId,
    Guid? TreatmentId,
    DateTime? UpdatedAtUtc);

public sealed record PetHistoryLabResultItemDto(
    Guid Id,
    DateTime? ResultDateUtc,
    Guid ClinicId,
    string ClinicName,
    string TestName,
    Guid? ExaminationId,
    DateTime? UpdatedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LabResultStatus Status,
    DateTime RequestedAtUtc,
    bool AwaitingInterpretation);

public sealed record PetHistoryHospitalizationItemDto(
    Guid Id,
    DateTime AdmittedAtUtc,
    Guid ClinicId,
    string ClinicName,
    string Reason,
    DateTime? DischargedAtUtc,
    bool IsActive,
    Guid? ExaminationId,
    DateTime? UpdatedAtUtc);

public sealed record PetHistoryPaymentItemDto(
    Guid Id,
    DateTime PaidAtUtc,
    Guid ClinicId,
    string ClinicName,
    decimal Amount,
    string Currency,
    PaymentMethod Method);
