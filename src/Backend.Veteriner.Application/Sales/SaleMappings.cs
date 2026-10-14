using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Sales;

namespace Backend.Veteriner.Application.Sales;

internal static class SaleMappings
{
    /// <param name="charge">Satışın güncel ücreti; iptal edilmiş (okuma filtresiyle gizli) veya yoksa <c>null</c>.</param>
    /// <param name="batches">Satır partileri (lot/SKT için); anahtar parti kimliği.</param>
    public static SaleDto ToDto(
        this Sale sale,
        Client client,
        Charge? charge,
        IReadOnlyDictionary<Guid, StockBatch> batches)
        => new(
            sale.Id,
            sale.ClinicId,
            sale.ClientId,
            client.FullName,
            client.IsWalkIn,
            sale.IsVoided ? "Voided" : "Active",
            sale.SoldAtUtc,
            sale.TotalAmount,
            Sale.Currency,
            sale.Notes,
            sale.ChargeId,
            sale.PaymentId,
            charge?.AllocatedAmount ?? 0m,
            charge?.OutstandingAmount ?? 0m,
            sale.CreatedByUserId,
            sale.CreatedAtUtc,
            sale.RequestId,
            sale.VoidedAtUtc,
            sale.VoidedByUserId,
            sale.VoidReason,
            sale.VoidStockOutcome?.ToString(),
            sale.Lines.Select(l => l.ToDto(l.BatchId is { } id && batches.TryGetValue(id, out var b) ? b : null)).ToList());

    private static SaleLineDto ToDto(this SaleLine line, StockBatch? batch)
        => new(
            line.Id,
            line.ProductId,
            line.ProductName,
            line.Unit,
            line.Quantity,
            line.UnitPrice,
            line.LineAmount,
            line.BatchId,
            batch?.LotNumber,
            batch?.ExpiryDate);
}
