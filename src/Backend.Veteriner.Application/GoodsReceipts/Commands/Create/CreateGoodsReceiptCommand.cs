using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.GoodsReceipts.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.GoodsReceipts.Commands.Create;

/// <summary>
/// POST /api/v1/goods-receipts. Tek fatura/irsaliyeden gelen çok satırlı stok girişi: her satır mevcut
/// <c>CreateStockMovementCommand</c> (<c>In</c>) ile yeni parti açar; hepsi tek transaction'da yazılır, biri başarısız olursa hiçbiri kalmaz.
/// Zorunlu: <see cref="RequestId"/>, <see cref="ClinicId"/> ve 1–<see cref="MaxLines"/> arası <see cref="Lines"/>.
/// </summary>
public sealed record CreateGoodsReceiptCommand(
    Guid ClinicId,
    Guid RequestId,
    string? SupplierName,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    DateOnly? ReceivedDate,
    DateOnly? DispatchedDate,
    string? TransportMethod,
    string? ColdChainMonitor,
    string? Notes,
    IReadOnlyList<GoodsReceiptLineDto>? Lines)
    : IRequest<Result<GoodsReceiptCreateResult>>, ITransactionalRollbackOnFailureRequest
{
    public const int MaxLines = 100;
}
