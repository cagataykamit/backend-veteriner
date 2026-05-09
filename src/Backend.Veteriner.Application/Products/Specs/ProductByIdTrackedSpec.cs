using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Products.Specs;

public sealed class ProductByIdTrackedSpec : Specification<Product>
{
    public ProductByIdTrackedSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
