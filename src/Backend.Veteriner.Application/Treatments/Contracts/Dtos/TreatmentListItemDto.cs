namespace Backend.Veteriner.Application.Treatments.Contracts.Dtos;

public sealed record TreatmentListItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    DateTime TreatmentDateUtc,
    string Title,
    Guid? ExaminationId,
    DateTime? FollowUpDateUtc);
