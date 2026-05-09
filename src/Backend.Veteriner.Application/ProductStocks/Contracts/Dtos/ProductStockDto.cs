namespace Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;

/// <summary>Klinik bazlı ürün stok satırı (okuma).</summary>
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
    DateTime UpdatedAtUtc);
