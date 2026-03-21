namespace Backend.Veteriner.Application.Examinations.Contracts.Dtos;

public sealed record ExaminationDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    string VisitReason,
    string Findings,
    string? Assessment,
    string? Notes);
