using Backend.Veteriner.Application.Prescriptions;
using Backend.Veteriner.Application.Hbs.Contracts;

namespace Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;

/// <summary>
/// Reçete detayı. <see cref="Status"/>: <c>Active</c> veya <c>Cancelled</c> (RECORD-008); iptal alanları yalnız iptalde dolu,
/// <see cref="LateEntryReason"/> yalnız <see cref="AddedAfterFinalization"/> iken dolu.
/// </summary>
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
    DateTime? UpdatedAtUtc,
    IReadOnlyList<PrescriptionItemDto> Items,
    HbsEntryDto? Hbs = null,
    string Status = PrescriptionStatusText.Active,
    DateTime? CancelledAtUtc = null,
    string? CancelledByName = null,
    string? CancelReason = null,
    bool AddedAfterFinalization = false,
    string? LateEntryReason = null);
