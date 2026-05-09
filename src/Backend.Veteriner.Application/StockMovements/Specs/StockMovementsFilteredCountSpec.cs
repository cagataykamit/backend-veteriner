using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.StockMovements.Specs;

public sealed class StockMovementsFilteredCountSpec : Specification<StockMovement>
{
    public StockMovementsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        Guid? productId,
        Guid? productCategoryId,
        StockMovementType? movementType,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        string? searchContainsLikePattern)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId);
        ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.Where(m => m.Product!.TenantId == tenantId);

        if (productId.HasValue)
            Query.Where(m => m.ProductId == productId.Value);

        if (productCategoryId.HasValue)
            Query.Where(m => m.Product!.ProductCategoryId == productCategoryId.Value);

        if (movementType.HasValue)
            Query.Where(m => m.MovementType == movementType.Value);

        if (dateFromUtc.HasValue)
            Query.Where(m => m.OccurredAtUtc >= dateFromUtc.Value);

        if (dateToUtc.HasValue)
            Query.Where(m => m.OccurredAtUtc <= dateToUtc.Value);

        if (searchContainsLikePattern is not null)
        {
            var pattern = searchContainsLikePattern;
            Query.Where(m =>
                EF.Functions.Like(m.Product!.Name, pattern) ||
                (m.Product!.Sku != null && EF.Functions.Like(m.Product.Sku, pattern)) ||
                (m.Product.Barcode != null && EF.Functions.Like(m.Product.Barcode, pattern)) ||
                (m.Reason != null && EF.Functions.Like(m.Reason, pattern)) ||
                (m.Notes != null && EF.Functions.Like(m.Notes, pattern)) ||
                (m.ReferenceType != null && EF.Functions.Like(m.ReferenceType, pattern)));
        }
    }

    private void ApplyClinicScope(Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            Query.Where(m => m.ClinicId == clinicId.Value);
        else if (accessibleClinicIds is not null)
        {
            if (accessibleClinicIds.Count == 0)
                Query.Where(m => false);
            else
                Query.Where(m => accessibleClinicIds.Contains(m.ClinicId));
        }
    }
}
