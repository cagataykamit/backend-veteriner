using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Products.Specs;

public sealed class ProductsByIdsSpec : Specification<Product>
{
    public ProductsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking().Where(x => x.TenantId == tenantId && ids.Contains(x.Id));
    }
}
