using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ProductStocks.Specs;

/// <summary>
/// Güncelleme için ProductStock — tracking için <see cref="Specification{T}.Query"/> üzerinde AsNoTracking kullanılmaz.
/// </summary>
public sealed class ProductStockForUpdateSpec : Specification<ProductStock>
{
    public ProductStockForUpdateSpec(Guid tenantId, Guid clinicId, Guid productId)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.ProductId == productId);
    }
}
