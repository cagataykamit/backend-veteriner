namespace Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;

public sealed record HospitalizationListItemDto(
    Guid Id,
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
    bool IsActive);
