using Backend.Veteriner.Application.Ai.Drafts;

namespace Backend.Veteriner.Application.CareInstructions.Contracts.Dtos;

/// <param name="AiProvenance">Talimat AI taslağından oluştuysa iz (AI-004, AI-010); yoksa boş.</param>
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
    string RowVersion,
    IReadOnlyList<AiProvenanceDto>? AiProvenance = null);
