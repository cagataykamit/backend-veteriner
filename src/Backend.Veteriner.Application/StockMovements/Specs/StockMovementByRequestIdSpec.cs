using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockMovements.Specs;

public sealed class StockMovementByRequestIdSpec : Specification<StockMovement>
{
    public StockMovementByRequestIdSpec(Guid tenantId, Guid requestId)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId && m.RequestId == requestId);
    }
}
