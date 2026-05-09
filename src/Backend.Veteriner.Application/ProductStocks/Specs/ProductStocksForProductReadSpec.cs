using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ProductStocks.Specs;

/// <summary>Tek ürün için klinik kapsamına göre stok satırları (sıralı liste).</summary>
public sealed class ProductStocksForProductReadSpec : Specification<ProductStock>
{
    public ProductStocksForProductReadSpec(
        Guid tenantId,
        Guid productId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.AsNoTracking();
        Query.Where(s => s.TenantId == tenantId && s.ProductId == productId);
        ApplyClinicScope(clinicId, accessibleClinicIds);

        Query
            .Include(s => s.Product!)
                .ThenInclude(p => p.Category);

        Query
            .OrderByDescending(s => s.UpdatedAtUtc)
            .ThenByDescending(s => s.Id);
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
