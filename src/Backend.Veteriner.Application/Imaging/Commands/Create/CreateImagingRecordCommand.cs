using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imaging.Commands.Create;

/// <summary>POST /api/v1/imaging-records. Dosyalar ayrı çağrıyla yüklenir (ASAMA6 L6).</summary>
public sealed record CreateImagingRecordCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime PerformedAtUtc,
    ImagingModality Modality,
    string? Description,
    string? ClinicalNote)
    : IRequest<Result<Guid>>, IAuditableRequest
{
    public string AuditAction => "ImagingRecord.Create";
    public string? AuditTarget => $"PetId={PetId}";
}
