using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Products.Commands.Update;

/// <summary>
/// Sınıf ve şekil alanlarında kısmi güncelleme: <c>null</c> = dokunma; metin alanlarında boş string temizler.
/// KDV oranı ve alış fiyatında da <c>null</c> = dokunma; temizlemek için <see cref="ClearVatRate"/> / <see cref="ClearPurchasePrice"/>
/// (değerle birlikte verilemez; BATCH_STOCK_API_CONTRACT.md §2.1).
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
    string? Packaging = null,
    string? Manufacturer = null,
    decimal? VatRate = null,
    decimal? PurchasePrice = null,
    bool ClearVatRate = false,
    bool ClearPurchasePrice = false)
    : IRequest<Result>;
