using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ProductCategories.Specs;

public sealed class ProductCategoryByIdSpec : Specification<ProductCategory>
{
    public ProductCategoryByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
