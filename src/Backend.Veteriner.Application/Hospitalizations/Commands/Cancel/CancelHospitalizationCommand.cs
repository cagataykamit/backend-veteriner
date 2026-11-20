using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Cancel;

/// <summary>
/// POST /api/v1/hospitalizations/{id}/cancel (ADR-043 AK-5). Gerekçe 5–500 karakter; <see cref="RowVersion"/> zorunludur.
/// </summary>
public sealed record CancelHospitalizationCommand(Guid Id, string? Reason, string? RowVersion)
    : IRequest<Result<HospitalizationWriteResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Hospitalization.Cancel";
    public string? AuditTarget => $"HospitalizationId={Id}";
}
