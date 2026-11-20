using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Treatments.Commands.Cancel;

/// <summary>
/// POST /api/v1/treatments/{id}/cancel (ADR-043 AK-2). Gerekçe 5–500 karakter; <see cref="RowVersion"/> zorunludur.
/// </summary>
public sealed record CancelTreatmentCommand(Guid Id, string? Reason, string? RowVersion)
    : IRequest<Result>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Treatment.Cancel";
    public string? AuditTarget => $"TreatmentId={Id}";
}
