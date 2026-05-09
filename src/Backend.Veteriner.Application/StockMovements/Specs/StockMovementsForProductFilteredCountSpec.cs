using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockMovements.Specs;

public sealed class StockMovementsForProductFilteredCountSpec : Specification<StockMovement>
{
    public StockMovementsForProductFilteredCountSpec(
        Guid tenantId,
        Guid productId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        StockMovementType? movementType,
        DateTime? dateFromUtc,
        DateTime? dateToUtc)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId && m.ProductId == productId);
        ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.Where(m => m.Product!.TenantId == tenantId);

        if (movementType.HasValue)
            Query.Where(m => m.MovementType == movementType.Value);

        if (dateFromUtc.HasValue)
            Query.Where(m => m.OccurredAtUtc >= dateFromUtc.Value);

        if (dateToUtc.HasValue)
            Query.Where(m => m.OccurredAtUtc <= dateToUtc.Value);
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
