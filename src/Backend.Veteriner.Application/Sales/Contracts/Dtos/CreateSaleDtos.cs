namespace Backend.Veteriner.Application.Sales.Contracts.Dtos;

/// <summary>Satış satırı isteği. <see cref="UnitPrice"/> yoksa ürün fiyatı; <see cref="BatchId"/> yoksa FEFO.</summary>
public sealed record CreateSaleLineDto(Guid ProductId, decimal Quantity, decimal? UnitPrice, Guid? BatchId);

/// <summary>
/// Satış anı ödemesi. <see cref="Method"/> <c>Cash|Card|Transfer</c>; <see cref="Amount"/> yoksa satış toplamı;
/// <see cref="PaidAtUtc"/> yoksa şimdi.
/// </summary>
public sealed record CreateSalePaymentDto(string? Method, decimal? Amount, DateTime? PaidAtUtc, string? Notes);
