using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockMovements.Specs;

public sealed class StockMovementsForProductFilteredPagedSpec : Specification<StockMovement>
{
    public StockMovementsForProductFilteredPagedSpec(
        Guid tenantId,
        Guid productId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        StockMovementType? movementType,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        int page,
        int pageSize)
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

        Query.Include(m => m.Product!);

        Query
            .OrderByDescending(m => m.OccurredAtUtc)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
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
