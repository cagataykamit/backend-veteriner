using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ProductCategories.Specs;

public sealed class ProductCategoryByTenantAndNameSpec : Specification<ProductCategory>
{
    public ProductCategoryByTenantAndNameSpec(Guid tenantId, string normalizedName, Guid? excludeId = null)
    {
        Query.Where(x => x.TenantId == tenantId && x.Name.ToLower() == normalizedName);
        if (excludeId.HasValue)
            Query.Where(x => x.Id != excludeId.Value);
    }
}
