namespace Backend.Veteriner.Application.Administrations.Contracts.Dtos;

/// <summary>Uygulama kaydı. <see cref="Status"/>: <c>Active</c> veya <c>Voided</c>; iptal alanları (<see cref="VoidStockOutcome"/>: <c>StockReturned</c>/<c>WrittenOff</c> dahil) yalnız <c>Voided</c> iken dolu;
/// <see cref="LateEntryReason"/> yalnız <see cref="AddedAfterFinalization"/> iken dolu.</summary>
public sealed record AdministrationDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    Guid? TreatmentId,
    Guid ProductId,
    string ProductName,
    string? ProductSku,
    string Unit,
    decimal Quantity,
    DateTime AdministeredAtUtc,
    string? Notes,
    string Status,
    DateTime? VoidedAtUtc,
    Guid? VoidedByUserId,
    string? VoidReason,
    string? VoidStockOutcome,
    bool AddedAfterFinalization,
    string? LateEntryReason,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid? RequestId,
    Guid? BatchId = null,
    string? LotNumber = null,
    DateOnly? ExpiryDate = null);

/// <summary>Düşümden sonraki stok durumu; düşük stok yalnız bilgidir, kaydı engellemez.</summary>
public sealed record AdministrationStockDto(decimal QuantityOnHand, decimal MinimumStockLevel, bool IsBelowMinimum);

/// <summary>Oluşturma sonucu; <see cref="Created"/> false ise aynı <c>requestId</c> ile tekrar istek ilk kaydı döndürmüştür.</summary>
public sealed record AdministrationCreateResultDto(bool Created, AdministrationDto Administration, AdministrationStockDto Stock);
