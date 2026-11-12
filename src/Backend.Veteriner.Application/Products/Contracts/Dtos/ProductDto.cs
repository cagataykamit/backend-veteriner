using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Products.Contracts.Dtos;

/// <param name="VatRate">Ürünün KDV oranı (%); <c>null</c> = klinik varsayılanı (BATCH_STOCK_API_CONTRACT.md §2.1).</param>
/// <param name="PurchasePrice">Birim alış fiyatı (TL), yalnız bilgi.</param>
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
    bool IsActive,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ProductKind Kind,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ControlledSubstanceClass ControlledClass,
    string? PharmacologicalForm,
    string? PresentationForm,
    string? Packaging,
    string? Manufacturer,
    decimal? VatRate,
    decimal? PurchasePrice);
