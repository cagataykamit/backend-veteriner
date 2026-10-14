using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Administrations;

internal static class AdministrationMappings
{
    public static AdministrationDto ToDto(this Administration a, Product product, StockBatch? batch = null)
        => new(
            a.Id,
            a.ClinicId,
            a.PetId,
            a.ExaminationId,
            a.TreatmentId,
            a.ProductId,
            product.Name,
            product.Sku,
            product.Unit,
            a.Quantity,
            a.AdministeredAtUtc,
            a.Notes,
            a.IsVoided ? "Voided" : "Active",
            a.VoidedAtUtc,
            a.VoidedByUserId,
            a.VoidReason,
            a.VoidStockOutcome?.ToString(),
            a.AddedAfterFinalization,
            a.LateEntryReason,
            a.CreatedByUserId,
            a.CreatedAtUtc,
            a.RequestId,
            a.BatchId,
            batch?.LotNumber,
            batch?.ExpiryDate,
            a.VaccinationId);

    public static AdministrationStockDto ToDto(this ProductStock stock, decimal expiredQuantity)
    {
        var available = ExpiredStockReader.Available(stock.QuantityOnHand, expiredQuantity);
        return new(stock.QuantityOnHand, stock.MinimumStockLevel, available < stock.MinimumStockLevel, available, expiredQuantity);
    }
}
