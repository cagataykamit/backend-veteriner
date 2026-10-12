using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockMovements.Specs;

public sealed class StockMovementByIdSpec : Specification<StockMovement>
{
    public StockMovementByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId && m.Id == id);
    }
}
