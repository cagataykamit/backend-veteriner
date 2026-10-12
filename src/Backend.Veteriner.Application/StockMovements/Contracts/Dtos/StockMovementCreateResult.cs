namespace Backend.Veteriner.Application.StockMovements.Contracts.Dtos;

/// <summary>Yazma sonucu: <see cref="Created"/> false ise aynı requestId ile önceden kaydedilen hareket döner.</summary>
public sealed record StockMovementCreateResult(StockMovementDto Movement, bool Created);
