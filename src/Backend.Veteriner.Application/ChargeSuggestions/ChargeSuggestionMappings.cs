using Backend.Veteriner.Application.ChargeSuggestions.Contracts.Dtos;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;

namespace Backend.Veteriner.Application.ChargeSuggestions;

internal static class ChargeSuggestionMappings
{
    public static ChargeSuggestionDto ToDto(this ChargeSuggestion s, Guid clientId, string clientName, string petName)
        => new(
            s.Id,
            s.SourceType,
            s.SourceId,
            s.ClinicId,
            clientId,
            clientName,
            s.PetId,
            petName,
            s.ExaminationId,
            s.VisitId,
            s.ProductId,
            s.Description,
            s.Quantity,
            s.Unit,
            s.SuggestedUnitPrice,
            Charge.DefaultCurrency,
            s.OccurredAtUtc,
            s.Status.ToString(),
            s.ChargeId,
            s.ResolvedAtUtc,
            s.WaiveReason);
}
