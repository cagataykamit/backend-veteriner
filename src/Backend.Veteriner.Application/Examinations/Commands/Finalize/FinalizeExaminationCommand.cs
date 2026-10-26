using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Examinations.Commands.Finalize;

/// <summary>
/// Muayeneyi kesinleştirir (<c>Examinations.Finalize</c>). <see cref="RowVersion"/>: istemcinin gördüğü sürüm (Base64); zorunlu.
/// </summary>
public sealed record FinalizeExaminationCommand(Guid Id, string? RowVersion)
    : IRequest<Result<ExaminationFinalizeResultDto>>, IAuditableRequest
{
    public string AuditAction => "Examination.Finalize";
    public string? AuditTarget => $"ExaminationId={Id}";
}
