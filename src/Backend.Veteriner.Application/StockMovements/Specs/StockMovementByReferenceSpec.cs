using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockMovements.Specs;

/// <summary>Kaynak benzersizlik anahtarı: kiracı, klinik, ürün, kaynak ve hareket tipi.</summary>
public sealed class StockMovementByReferenceSpec : Specification<StockMovement>
{
    public StockMovementByReferenceSpec(
        Guid tenantId,
        Guid clinicId,
        Guid productId,
        string referenceType,
        Guid referenceId,
        StockMovementType movementType)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId
            && m.ClinicId == clinicId
            && m.ProductId == productId
            && m.ReferenceType == referenceType
            && m.ReferenceId == referenceId
            && m.MovementType == movementType);
    }
}
