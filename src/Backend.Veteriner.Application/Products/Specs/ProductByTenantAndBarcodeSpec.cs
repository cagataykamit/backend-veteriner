using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Products.Specs;

/// <summary>Barkodu aynı olan ürün (pasif dahil); <paramref name="barcode"/> <see cref="Product.NormalizeBarcode"/> ile biçimlenmiş olmalı.</summary>
public sealed class ProductByTenantAndBarcodeSpec : Specification<Product>
{
    public ProductByTenantAndBarcodeSpec(Guid tenantId, string barcode, Guid? excludeId = null)
    {
        Query.AsNoTracking().Where(x => x.TenantId == tenantId && x.Barcode == barcode);
        if (excludeId.HasValue)
            Query.Where(x => x.Id != excludeId.Value);
    }
}
