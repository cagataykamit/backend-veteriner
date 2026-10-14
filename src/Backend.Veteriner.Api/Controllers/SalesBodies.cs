using Backend.Veteriner.Application.Sales.Contracts.Dtos;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// POST /sales gövdesi. <c>ClientId</c> yoksa Peşin müşteri; <c>Payment</c> Peşin müşteride zorunlu ve tam tutardır.
/// Satır <c>UnitPrice</c> yoksa ürün fiyatı, <c>BatchId</c> yoksa FEFO kullanılır.
/// </summary>
public sealed class CreateSaleBody
{
    public Guid? ClinicId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid RequestId { get; init; }
    public string? Notes { get; init; }
    public List<CreateSaleLineDto>? Lines { get; init; }
    public CreateSalePaymentDto? Payment { get; init; }
}

/// <summary>POST /sales/{id}/void gövdesi. <c>StockOutcome</c> verilmezse <c>StockReturned</c>.</summary>
public sealed class VoidSaleBody
{
    public string? Reason { get; init; }
    public string? StockOutcome { get; init; }
}
