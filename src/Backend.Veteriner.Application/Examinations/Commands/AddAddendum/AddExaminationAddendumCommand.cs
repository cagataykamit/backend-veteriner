using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Commands.AddAddendum;

/// <summary>
/// Kesinleşmiş muayeneye gerekçeli ek not (<c>Examinations.Addendum</c>). Klinik metin audit yüküne yazılmaz.
/// </summary>
public sealed record AddExaminationAddendumCommand(
    Guid ExaminationId,
    [property: JsonIgnore] string? Reason,
    [property: JsonIgnore] string? Text)
    : IRequest<Result<ExaminationAddendumDto>>, IAuditableRequest
{
    public string AuditAction => "Examination.AddAddendum";
    public string? AuditTarget => $"ExaminationId={ExaminationId}";
}
