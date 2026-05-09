using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ProductCategories.Specs;

public sealed class ProductCategoriesByIdsSpec : Specification<ProductCategory>
{
    public ProductCategoriesByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking().Where(x => x.TenantId == tenantId && ids.Contains(x.Id));
    }
}
