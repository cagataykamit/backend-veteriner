using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ProductStocks.Specs;

/// <summary>
/// Id ile güncelleme — ürün/kategori adları için <see cref="Product.Category"/> dahil.
/// </summary>
public sealed class ProductStockForUpdateByIdSpec : Specification<ProductStock>
{
    public ProductStockForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query
            .Where(x => x.TenantId == tenantId && x.Id == id)
            .Include(x => x.Product!)
                .ThenInclude(p => p.Category);
    }
}
