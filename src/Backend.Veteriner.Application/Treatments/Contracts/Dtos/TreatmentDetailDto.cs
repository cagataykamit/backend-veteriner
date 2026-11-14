namespace Backend.Veteriner.Application.Treatments.Contracts.Dtos;

public sealed record TreatmentDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? ExaminationId,
    DateTime TreatmentDateUtc,
    string Title,
    string Description,
    string? Notes,
    DateTime? FollowUpDateUtc,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string? RowVersion = null,
    DateTime? CancelledAtUtc = null,
    string? CancelReason = null,
    bool AddedAfterFinalization = false,
    string? LateEntryReason = null);
