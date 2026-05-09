using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ProductStocks.Specs;

public sealed class ProductStocksFilteredCountSpec : Specification<ProductStock>
{
    public ProductStocksFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        Guid? productCategoryId,
        Guid? productId,
        bool? isBelowMinimum,
        bool? isActiveProduct,
        string? searchContainsLikePattern)
    {
        Query.AsNoTracking();
        Query.Where(s => s.TenantId == tenantId);
        ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.Where(s => s.Product!.TenantId == tenantId);

        if (productCategoryId.HasValue)
            Query.Where(s => s.Product!.ProductCategoryId == productCategoryId.Value);

        if (productId.HasValue)
            Query.Where(s => s.ProductId == productId.Value);

        if (isActiveProduct.HasValue)
            Query.Where(s => s.Product!.IsActive == isActiveProduct.Value);

        if (isBelowMinimum.HasValue)
        {
            if (isBelowMinimum.Value)
                Query.Where(s => s.QuantityOnHand < s.MinimumStockLevel);
            else
                Query.Where(s => s.QuantityOnHand >= s.MinimumStockLevel);
        }

        if (searchContainsLikePattern is not null)
        {
            var pattern = searchContainsLikePattern;
            Query.Where(s =>
                EF.Functions.Like(s.Product!.Name, pattern) ||
                (s.Product!.Sku != null && EF.Functions.Like(s.Product.Sku, pattern)) ||
                (s.Product.Barcode != null && EF.Functions.Like(s.Product.Barcode, pattern)));
        }
    }

    private void ApplyClinicScope(Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            Query.Where(s => s.ClinicId == clinicId.Value);
        else if (accessibleClinicIds is not null)
        {
            if (accessibleClinicIds.Count == 0)
                Query.Where(s => false);
            else
                Query.Where(s => accessibleClinicIds.Contains(s.ClinicId));
        }
    }
}
