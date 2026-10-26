using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Products.Commands.Update;

/// <summary>
/// Sınıf ve şekil alanlarında kısmi güncelleme: <c>null</c> = dokunma; metin alanlarında boş string temizler.
/// </summary>
public sealed record UpdateProductCommand(
    Guid Id,
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
    : IRequest<Result>;
