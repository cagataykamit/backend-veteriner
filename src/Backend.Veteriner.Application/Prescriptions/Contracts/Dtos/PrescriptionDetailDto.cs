namespace Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;

public sealed record PrescriptionDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? ExaminationId,
    Guid? TreatmentId,
    DateTime PrescribedAtUtc,
    string Title,
    string Content,
    string? Notes,
    DateTime? FollowUpDateUtc,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
