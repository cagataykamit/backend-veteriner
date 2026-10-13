using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;

/// <summary>
/// Okuyucunun yüklediği ham hareket (mapper girdisi). <paramref name="ReversedMovementType"/> yalnız <c>Reversal</c> için,
/// <paramref name="PreviousBatchBalance"/> yalnız partili <c>Adjustment</c> için doludur;
/// <paramref name="AdministrationTarget"/> uygulama çıkışında <c>Hasta (Sahip)</c> metnidir.
/// </summary>
public sealed record ControlledSubstanceMovement(
    Guid MovementId,
    Guid ProductId,
    StockMovementType MovementType,
    DateTime OccurredAtUtc,
    decimal Quantity,
    string? Reason,
    string? ReferenceType,
    string ProductName,
    string? PharmacologicalForm,
    string? PresentationForm,
    string? Packaging,
    Guid? BatchId,
    string? LotNumber,
    string? SupplierName,
    string? InvoiceNumber,
    DateOnly? ReceivedDate,
    decimal? BatchBalanceAfter,
    decimal? PreviousBatchBalance,
    StockMovementType? ReversedMovementType,
    string? AdministrationTarget);
