using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ProductCategories.Specs;

public sealed class ProductCategoryByTenantAndNameSpec : Specification<ProductCategory>
{
    public ProductCategoryByTenantAndNameSpec(Guid tenantId, string name, Guid? excludeId = null)
    {
        var trimmedName = name.Trim();
        Query.Where(x => x.TenantId == tenantId && x.Name == trimmedName);
        if (excludeId.HasValue)
            Query.Where(x => x.Id != excludeId.Value);
    }
}
