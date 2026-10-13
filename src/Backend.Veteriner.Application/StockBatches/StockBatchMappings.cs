using Backend.Veteriner.Application.StockBatches.Contracts.Dtos;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockBatches;

internal static class StockBatchMappings
{
    public static StockBatchDto ToDto(this StockBatch b, Product product, DateOnly today)
        => new(
            b.Id,
            b.ClinicId,
            b.ProductId,
            product.Name,
            product.Sku,
            product.Unit,
            product.Kind,
            product.ControlledClass,
            b.LotNumber,
            b.ExpiryDate,
            b.InitialQuantity,
            b.RemainingQuantity,
            b.SupplierName,
            b.InvoiceNumber,
            b.InvoiceDate,
            b.ReceivedDate,
            b.DispatchedDate,
            b.TransportMethod,
            b.ColdChainMonitor,
            b.IsExpiredOn(today),
            b.CreatedAtUtc,
            b.CreatedByUserId);
}
