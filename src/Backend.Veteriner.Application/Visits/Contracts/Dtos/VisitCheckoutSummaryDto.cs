using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Contracts.Dtos;

/// <summary>
/// GET /visits/{id}/checkout-summary ("Hesabı kapat", CHECKOUT-001). <see cref="Charges"/> yalnız <c>Charges.Read</c> varsa dolar.
/// Müşteri hayvanın güncel sahibidir (ücretin müşterisiyle aynı kural). Sözleşme: <c>docs/VISIT_CHECKOUT_API_CONTRACT.md</c>.
/// </summary>
public sealed record VisitCheckoutSummaryDto(
    Guid VisitId,
    Guid ClinicId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VisitCareStatus CareStatus,
    DateTime ArrivedAtUtc,
    DateTime? CompletedAtUtc,
    bool IsVoided,
    string? ResponsibleVeterinarianName,
    string? ArrivalReason,
    Guid PetId,
    string PetName,
    string? SpeciesName,
    Guid ClientId,
    string ClientName,
    string? ClientPhone,
    string Currency,
    VisitCheckoutChargesDto? Charges);

public sealed record VisitCheckoutChargesDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VisitChargeIndicator? ChargeIndicator,
    IReadOnlyList<VisitCheckoutPendingItemDto> PendingItems,
    IReadOnlyList<VisitCheckoutChargeDto> VisitCharges,
    IReadOnlyList<VisitCheckoutOtherChargeDto> OtherOpenCharges,
    decimal CreditBalance);

/// <summary>Ücret bekleyen kalem; <see cref="SuggestedUnitPrice"/> yoksa fiyatı resepsiyon girer.</summary>
public sealed record VisitCheckoutPendingItemDto(
    Guid Id,
    string SourceType,
    Guid PetId,
    string PetName,
    string Description,
    decimal Quantity,
    string Unit,
    decimal? SuggestedUnitPrice,
    DateTime OccurredAtUtc);

public sealed record VisitCheckoutChargeDto(
    Guid ChargeId,
    Guid? PetId,
    string? PetName,
    string Description,
    decimal Amount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    DateTime CreatedAtUtc);

/// <summary>Müşterinin bu gelişe ait olmayan açık ücreti (aynı klinik); panelde seçili gelmez.</summary>
public sealed record VisitCheckoutOtherChargeDto(
    Guid ChargeId,
    Guid? VisitId,
    DateTime? VisitArrivedAtUtc,
    Guid? PetId,
    string? PetName,
    string Description,
    decimal Amount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    DateTime CreatedAtUtc);
