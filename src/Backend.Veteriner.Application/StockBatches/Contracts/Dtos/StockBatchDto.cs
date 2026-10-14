using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockBatches.Contracts.Dtos;

/// <summary>Stok partisi (lot) okuma modeli. <see cref="IsExpired"/>: SKT bugünden (İstanbul günü) önce.</summary>
public sealed record StockBatchDto(
    Guid Id,
    Guid ClinicId,
    Guid ProductId,
    string ProductName,
    string? ProductSku,
    string Unit,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ProductKind ProductKind,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ControlledSubstanceClass ControlledClass,
    string? LotNumber,
    DateOnly? ExpiryDate,
    decimal InitialQuantity,
    decimal RemainingQuantity,
    string? SupplierName,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    DateOnly? ReceivedDate,
    DateOnly? DispatchedDate,
    string? TransportMethod,
    string? ColdChainMonitor,
    bool IsExpired,
    DateTime CreatedAtUtc,
    Guid? CreatedByUserId);

/// <summary>
/// Ürün bazlı parti bakiyesi: <see cref="UnbatchedQuantity"/> parti bilgisi olmayan (eski/devir) bakiyedir
/// (<c>QuantityOnHand − BatchedQuantity</c>); <see cref="Batches"/> kalanı olan partiler FEFO sıralıdır.
/// <see cref="ExpiredQuantity"/> süresi geçmiş partilerin kalanıdır; <see cref="AvailableQuantity"/> = <c>QuantityOnHand − ExpiredQuantity</c>.
/// </summary>
public sealed record ProductBatchBalanceDto(
    Guid ProductId,
    Guid ClinicId,
    string Unit,
    decimal QuantityOnHand,
    decimal BatchedQuantity,
    decimal UnbatchedQuantity,
    IReadOnlyList<StockBatchDto> Batches,
    decimal AvailableQuantity,
    decimal ExpiredQuantity);
