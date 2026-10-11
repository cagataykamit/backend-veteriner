using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Contracts.Dtos;

public sealed record ExaminationAddendumDto(
    Guid Id,
    Guid ExaminationId,
    string Reason,
    string Text,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    string? CreatedByName)
{
    public static ExaminationAddendumDto From(ExaminationAddendum a, string? createdByName)
        => new(a.Id, a.ExaminationId, a.Reason, a.Text, a.CreatedAtUtc, a.CreatedByUserId, createdByName);
}
