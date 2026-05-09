using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ProductCategories.Specs;

public sealed class ProductCategoriesFilteredPagedSpec : Specification<ProductCategory>
{
    public ProductCategoriesFilteredPagedSpec(
        Guid tenantId,
        int page,
        int pageSize,
        string? searchPattern,
        bool? isActive)
    {
        Query.AsNoTracking().Where(x => x.TenantId == tenantId);

        if (isActive.HasValue)
            Query.Where(x => x.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(searchPattern))
            Query.Where(x => EF.Functions.Like(x.Name, searchPattern));

        Query
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
