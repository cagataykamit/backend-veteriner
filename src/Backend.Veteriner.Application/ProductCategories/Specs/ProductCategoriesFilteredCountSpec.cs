using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ProductCategories.Specs;

public sealed class ProductCategoriesFilteredCountSpec : Specification<ProductCategory>
{
    public ProductCategoriesFilteredCountSpec(Guid tenantId, string? searchPattern, bool? isActive)
    {
        Query.Where(x => x.TenantId == tenantId);
        if (isActive.HasValue)
            Query.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(searchPattern))
            Query.Where(x => EF.Functions.Like(x.Name, searchPattern));
    }
}
