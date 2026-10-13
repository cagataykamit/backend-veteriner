using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Commands.Create;

public sealed record CreateProductCommand(
    Guid? ProductCategoryId,
    string Name,
    string? Sku,
    string? Barcode,
    string? Description,
    string Unit,
    decimal UnitPrice,
    string Currency,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ProductKind? Kind = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ControlledSubstanceClass? ControlledClass = null,
    string? PharmacologicalForm = null,
    string? PresentationForm = null,
    string? Packaging = null)
    : IRequest<Result<ProductDto>>;
