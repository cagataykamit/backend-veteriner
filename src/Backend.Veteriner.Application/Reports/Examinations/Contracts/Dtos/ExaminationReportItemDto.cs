namespace Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;

public sealed record ExaminationReportItemDto(
    Guid ExaminationId,
    DateTime ExaminedAtUtc,
    Guid ClinicId,
    string ClinicName,
    Guid ClientId,
    string ClientName,
    Guid PetId,
    string PetName,
    Guid? AppointmentId,
    string VisitReason,
    string Findings,
    string? Assessment,
    string? Notes);
