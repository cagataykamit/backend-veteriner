namespace Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;

public sealed record HospitalizationDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? ExaminationId,
    DateTime AdmittedAtUtc,
    DateTime? PlannedDischargeAtUtc,
    DateTime? DischargedAtUtc,
    string Reason,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    bool IsActive);
