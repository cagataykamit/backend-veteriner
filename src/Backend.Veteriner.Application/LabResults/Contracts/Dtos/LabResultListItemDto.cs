namespace Backend.Veteriner.Application.LabResults.Contracts.Dtos;

public sealed record LabResultListItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    DateTime ResultDateUtc,
    string TestName,
    Guid? ExaminationId);
