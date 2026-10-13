using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Products;

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
    bool IsActive,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ProductKind Kind,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ControlledSubstanceClass ControlledClass,
    string? PharmacologicalForm,
    string? PresentationForm,
    string? Packaging);
