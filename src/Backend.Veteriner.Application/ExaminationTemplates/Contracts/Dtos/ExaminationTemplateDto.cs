using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;

/// <summary>Muayene şablonu. <c>IsSystem</c> true ise salt okunur sistem varsayılanıdır (ürün satırı yoktur).</summary>
public sealed record ExaminationTemplateDto(
    Guid Id,
    string Name,
    string? VisitReason,
    string? Findings,
    string? Plan,
    bool IsActive,
    bool IsSystem,
    IReadOnlyList<ExaminationTemplateLineDto> Lines,
    string RowVersion);

/// <summary>
/// Şablon satırı; ürün alanları ürün kartının <b>güncel</b> hâlidir (şablonda fiyat/ad saklanmaz). <c>Kind = Service</c> satırı
/// stok düşmez, uygulamada "ücret bekliyor" kalemi doğurur; diğerleri uygulama kaydı olur.
/// </summary>
public sealed record ExaminationTemplateLineDto(
    Guid ProductId,
    string ProductName,
    string Unit,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ProductKind ProductKind,
    bool ProductIsActive,
    decimal Quantity,
    decimal CurrentUnitPrice,
    string Currency);

/// <summary>Şablon satırı isteği (sıra listedeki konumdur).</summary>
public sealed record ExaminationTemplateLineRequest(Guid ProductId, decimal Quantity);
