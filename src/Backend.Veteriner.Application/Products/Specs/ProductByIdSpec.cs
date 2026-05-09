using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Products.Specs;

public sealed class ProductByIdSpec : Specification<Product>
{
    public ProductByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
