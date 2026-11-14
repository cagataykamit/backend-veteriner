using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Cancel;

/// <summary>
/// POST /api/v1/vaccinations/{id}/cancel (ADR-043 AK-3). Gerekçe 5–500 karakter; <see cref="RowVersion"/> zorunludur.
/// </summary>
public sealed record CancelVaccinationCommand(Guid Id, string? Reason, string? RowVersion)
    : IRequest<Result>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "Vaccination.Cancel";
    public string? AuditTarget => $"VaccinationId={Id}";
}
