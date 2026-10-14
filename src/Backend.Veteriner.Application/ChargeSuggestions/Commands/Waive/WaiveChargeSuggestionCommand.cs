using Backend.Veteriner.Application.ChargeSuggestions.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ChargeSuggestions.Commands.Waive;

/// <summary>
/// POST /api/v1/charge-suggestions/{id}/waive. <c>Charges.Create</c> ve <see cref="Reason"/> (5–500 karakter) gerekir.
/// Kalem ücretsiz sayılır, listeden düşer; geri alınmaz.
/// </summary>
public sealed record WaiveChargeSuggestionCommand(Guid Id, string? Reason)
    : IRequest<Result<ChargeSuggestionDto>>, IAuditableRequest
{
    public string AuditAction => "ChargeSuggestion.Waive";
    public string? AuditTarget => $"ChargeSuggestionId={Id}";
}
