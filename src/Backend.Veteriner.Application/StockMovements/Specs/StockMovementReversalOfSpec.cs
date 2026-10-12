using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockMovements.Specs;

public sealed class StockMovementReversalOfSpec : Specification<StockMovement>
{
    public StockMovementReversalOfSpec(Guid tenantId, Guid reversedMovementId)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId && m.ReversesMovementId == reversedMovementId);
    }
}
