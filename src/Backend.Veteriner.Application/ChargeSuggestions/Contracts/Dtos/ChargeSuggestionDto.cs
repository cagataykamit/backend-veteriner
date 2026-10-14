namespace Backend.Veteriner.Application.ChargeSuggestions.Contracts.Dtos;

/// <summary>
/// "Ücret bekliyor" kalemi. <see cref="ClientId"/>/<see cref="ClientName"/> hayvanın güncel sahibidir (ücretin müşterisi);
/// <see cref="SuggestedUnitPrice"/> kalem doğduğu andaki katalog fiyatıdır, yoksa <c>null</c> (resepsiyon girer).
/// </summary>
public sealed record ChargeSuggestionDto(
    Guid Id,
    string SourceType,
    Guid SourceId,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    Guid PetId,
    string PetName,
    Guid? ExaminationId,
    Guid? VisitId,
    Guid ProductId,
    string Description,
    decimal Quantity,
    string Unit,
    decimal? SuggestedUnitPrice,
    string Currency,
    DateTime OccurredAtUtc,
    string Status,
    Guid? ChargeId,
    DateTime? ResolvedAtUtc,
    string? WaiveReason);
