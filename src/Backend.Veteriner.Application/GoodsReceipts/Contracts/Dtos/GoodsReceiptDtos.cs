using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;

namespace Backend.Veteriner.Application.GoodsReceipts.Contracts.Dtos;

/// <summary>Mal kabul satırı: bir ürünün tek partisi. Ortak fatura/nakil bilgisi komut başlığındadır.</summary>
public sealed record GoodsReceiptLineDto(
    Guid ProductId,
    decimal Quantity,
    string? LotNumber = null,
    DateOnly? ExpiryDate = null,
    decimal? UnitCost = null,
    bool? AcknowledgeExpired = null);

/// <summary>Yazma sonucu: <see cref="Created"/> false ise aynı requestId ile önceden kaydedilen satırlar döner. <see cref="Lines"/> istek sırasındadır.</summary>
public sealed record GoodsReceiptCreateResult(bool Created, Guid ClinicId, IReadOnlyList<StockMovementDto> Lines);
