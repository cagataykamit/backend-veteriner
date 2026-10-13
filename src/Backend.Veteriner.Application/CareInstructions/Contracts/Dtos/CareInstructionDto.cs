namespace Backend.Veteriner.Application.CareInstructions.Contracts.Dtos;

public sealed record CareInstructionDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    Guid? HospitalizationId,
    Guid? ExaminationId,
    Guid? VisitId,
    string Text,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid? UpdatedByUserId,
    DateTime? UpdatedAtUtc,
    string RowVersion);
