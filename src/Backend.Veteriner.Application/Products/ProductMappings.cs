using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Products;

internal static class ProductMappings
{
    public static ProductDto ToDto(this Product p, string? categoryName)
        => new(
            p.Id,
            p.ProductCategoryId,
            categoryName,
            p.Name,
            p.Sku,
            p.Barcode,
            p.Description,
            p.Unit,
            p.UnitPrice,
            p.Currency,
            p.IsActive,
            p.Kind,
            p.ControlledClass,
            p.PharmacologicalForm,
            p.PresentationForm,
            p.Packaging);
}
