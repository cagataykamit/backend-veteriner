namespace Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;

/// <summary>
/// Klinik bazlı ürün stok satırı (okuma). <see cref="QuantityOnHand"/> toplamdır (süresi geçmiş dahil);
/// <see cref="AvailableQuantity"/> = toplam − <see cref="ExpiredQuantity"/>; <see cref="IsBelowMinimum"/> kullanılabilire bakar.
/// </summary>
public sealed record ProductStockDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? ProductSku,
    Guid? ProductCategoryId,
    string? ProductCategoryName,
    Guid ClinicId,
    string ClinicName,
    decimal QuantityOnHand,
    decimal MinimumStockLevel,
    bool IsBelowMinimum,
    DateTime UpdatedAtUtc,
    decimal AvailableQuantity,
    decimal ExpiredQuantity);
