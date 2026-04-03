namespace Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;

public sealed record PrescriptionListItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    DateTime PrescribedAtUtc,
    string Title,
    Guid? ExaminationId,
    Guid? TreatmentId,
    DateTime? FollowUpDateUtc);
