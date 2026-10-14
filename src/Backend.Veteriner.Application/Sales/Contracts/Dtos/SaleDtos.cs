namespace Backend.Veteriner.Application.Sales.Contracts.Dtos;

/// <summary>
/// Satış başlığı ve satırları (bilgi amaçlı fiş verisi; resmî belge değildir). <see cref="PaidAmount"/>/<see cref="OutstandingAmount"/>
/// ücretin güncel tahsis durumundan gelir; iptal edilmiş satışta ücret borç sayılmadığı için ikisi de 0'dır.
/// </summary>
public sealed record SaleDto(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    bool IsWalkIn,
    string Status,
    DateTime SoldAtUtc,
    decimal TotalAmount,
    string Currency,
    string? Notes,
    Guid? ChargeId,
    Guid? PaymentId,
    decimal PaidAmount,
    decimal OutstandingAmount,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid RequestId,
    DateTime? VoidedAtUtc,
    Guid? VoidedByUserId,
    string? VoidReason,
    string? VoidStockOutcome,
    IReadOnlyList<SaleLineDto> Lines);

public sealed record SaleLineDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineAmount,
    Guid? BatchId,
    string? LotNumber,
    DateOnly? ExpiryDate);

/// <summary>Satıştan sonraki ürün stok durumu; düşük stok yalnız bilgidir, satışı engellemez.</summary>
public sealed record SaleStockDto(
    Guid ProductId,
    decimal QuantityOnHand,
    decimal MinimumStockLevel,
    bool IsBelowMinimum,
    decimal AvailableQuantity,
    decimal ExpiredQuantity);

/// <summary>Oluşturma sonucu; <see cref="Created"/> false ise aynı <c>requestId</c> ile tekrar istek ilk satışı döndürmüştür.</summary>
public sealed record SaleCreateResultDto(bool Created, SaleDto Sale, IReadOnlyList<SaleStockDto> Stocks);

/// <summary>Peşin müşteri (kiracıda tek sistem müşterisi); <see cref="Created"/> bu çağrıda oluşturulup oluşturulmadığıdır.</summary>
public sealed record WalkInClientDto(Guid ClientId, string FullName, bool Created);
