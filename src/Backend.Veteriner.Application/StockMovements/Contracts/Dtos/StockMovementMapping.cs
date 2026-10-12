using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockMovements.Contracts.Dtos;

internal static class StockMovementMapping
{
    public static StockMovementDto ToDto(
        this StockMovement movement,
        Product product,
        string? productCategoryName,
        string clinicName)
        => new(
            movement.Id,
            movement.ProductId,
            product.Name,
            product.Sku,
            product.ProductCategoryId,
            productCategoryName,
            movement.ClinicId,
            clinicName,
            movement.MovementType,
            movement.Quantity,
            movement.UnitCost,
            movement.Reason,
            movement.ReferenceType,
            movement.ReferenceId,
            movement.OccurredAtUtc,
            movement.CreatedByUserId,
            movement.Notes,
            movement.CreatedAtUtc,
            movement.RequestId,
            movement.ReversesMovementId);
}
