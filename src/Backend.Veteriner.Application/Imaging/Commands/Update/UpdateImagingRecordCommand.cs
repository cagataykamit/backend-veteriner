using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imaging.Commands.Update;

/// <summary>PUT /api/v1/imaging-records/{id}: tam gövde, <see cref="RowVersion"/> zorunlu. Hasta ve klinik değişmez.</summary>
public sealed record UpdateImagingRecordCommand(
    Guid Id,
    Guid? ExaminationId,
    DateTime PerformedAtUtc,
    ImagingModality Modality,
    string? Description,
    string? ClinicalNote,
    string? RowVersion)
    : IRequest<Result<ImagingRecordWriteResultDto>>, IAuditableRequest
{
    public string AuditAction => "ImagingRecord.Update";
    public string? AuditTarget => $"ImagingRecordId={Id}";
}
