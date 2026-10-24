using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.ProductStocks.Specs;

public sealed class ProductStocksFilteredPagedSpec : Specification<ProductStock>
{
    public ProductStocksFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        Guid? productCategoryId,
        Guid? productId,
        bool? isBelowMinimum,
        bool? isActiveProduct,
        string? searchContainsLikePattern,
        DateOnly today,
        int page,
        int pageSize)
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
            Query.Where(ProductStockMinimumFilter.Create(isBelowMinimum.Value, today));

        if (searchContainsLikePattern is not null)
        {
            var pattern = searchContainsLikePattern;
            Query.Where(s =>
                EF.Functions.Like(s.Product!.Name, pattern) ||
                (s.Product!.Sku != null && EF.Functions.Like(s.Product.Sku, pattern)) ||
                (s.Product.Barcode != null && EF.Functions.Like(s.Product.Barcode, pattern)));
        }

        Query
            .Include(s => s.Product!)
                .ThenInclude(p => p.Category);

        Query
            .OrderByDescending(s => s.UpdatedAtUtc)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }

    private void ApplyClinicScope(Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
    }
}
