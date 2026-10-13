namespace Backend.Veteriner.Application.Reports.Registers.Vaccines;

/// <summary>
/// Aşı kayıt defteri satırı (bir aşı stok girişi). Parti alanları partisiz (eski) girişte null'dır;
/// <c>DeliveredDate</c> geliş/teslim tarihi, yoksa girişin İstanbul günüdür.
/// </summary>
public sealed record VaccineRegisterItemDto(
    int RowNumber,
    Guid MovementId,
    Guid ProductId,
    string TradeName,
    string? LotNumber,
    DateOnly? ExpiryDate,
    decimal TotalQuantity,
    string? SupplierName,
    string? TransportMethod,
    string? ColdChainMonitor,
    DateOnly? DispatchedDate,
    DateOnly DeliveredDate,
    DateOnly? InvoiceDate,
    string? InvoiceNumber,
    bool IsBatched,
    bool IsReversed);
