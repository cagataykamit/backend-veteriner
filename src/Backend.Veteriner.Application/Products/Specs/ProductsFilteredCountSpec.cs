using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Products.Specs;

public sealed class ProductsFilteredCountSpec : Specification<Product>
{
    public ProductsFilteredCountSpec(Guid tenantId, string? searchPattern, Guid? productCategoryId, bool? isActive)
    {
        Query.Where(x => x.TenantId == tenantId);

        if (productCategoryId.HasValue)
            Query.Where(x => x.ProductCategoryId == productCategoryId.Value);

        if (isActive.HasValue)
            Query.Where(x => x.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(searchPattern))
            Query.Where(x =>
                EF.Functions.Like(x.Name, searchPattern)
                || (x.Sku != null && EF.Functions.Like(x.Sku, searchPattern))
                || (x.Barcode != null && EF.Functions.Like(x.Barcode, searchPattern)));
    }
}
