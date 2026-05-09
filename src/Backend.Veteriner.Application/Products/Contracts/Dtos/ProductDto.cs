namespace Backend.Veteriner.Application.Products.Contracts.Dtos;

public sealed record ProductDto(
    Guid Id,
    Guid? ProductCategoryId,
    string? ProductCategoryName,
    string Name,
    string? Sku,
    string? Barcode,
    string? Description,
    string Unit,
    decimal UnitPrice,
    string Currency,
    bool IsActive);
