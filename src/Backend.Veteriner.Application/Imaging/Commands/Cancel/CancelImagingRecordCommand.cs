using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imaging.Commands.Cancel;

/// <summary>
/// POST /api/v1/imaging-records/{id}/cancel. Gerekçe 5–500 karakter; <see cref="RowVersion"/> zorunludur.
/// Kaydın bütün dosyaları aynı işlemde <c>Deleted</c> olur (erişim kapanır).
/// </summary>
public sealed record CancelImagingRecordCommand(Guid Id, string? Reason, string? RowVersion)
    : IRequest<Result<ImagingRecordWriteResultDto>>, IAuditableRequest
{
    public string AuditAction => "ImagingRecord.Cancel";
    public string? AuditTarget => $"ImagingRecordId={Id}";
}
