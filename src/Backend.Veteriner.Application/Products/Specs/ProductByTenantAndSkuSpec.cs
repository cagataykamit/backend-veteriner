using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Products.Specs;

public sealed class ProductByTenantAndSkuSpec : Specification<Product>
{
    public ProductByTenantAndSkuSpec(Guid tenantId, string normalizedSku, Guid? excludeId = null)
    {
        Query.Where(x => x.TenantId == tenantId && x.Sku != null && x.Sku.ToLower() == normalizedSku);
        if (excludeId.HasValue)
            Query.Where(x => x.Id != excludeId.Value);
    }
}
