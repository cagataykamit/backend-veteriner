using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Products.Specs;

public sealed class ProductsFilteredPagedSpec : Specification<Product>
{
    public ProductsFilteredPagedSpec(
        Guid tenantId,
        int page,
        int pageSize,
        string? searchPattern,
        Guid? productCategoryId,
        bool? isActive)
    {
        Query.AsNoTracking().Where(x => x.TenantId == tenantId);

        if (productCategoryId.HasValue)
            Query.Where(x => x.ProductCategoryId == productCategoryId.Value);

        if (isActive.HasValue)
            Query.Where(x => x.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(searchPattern))
            Query.Where(x =>
                EF.Functions.Like(x.Name, searchPattern)
                || (x.Sku != null && EF.Functions.Like(x.Sku, searchPattern))
                || (x.Barcode != null && EF.Functions.Like(x.Barcode, searchPattern)));

        Query
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
