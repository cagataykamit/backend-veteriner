namespace Backend.Veteriner.Application.Administrations.Contracts.Dtos;

/// <summary>Uygulama kaydı. <see cref="Status"/>: <c>Active</c> veya <c>Voided</c>; iptal alanları (<see cref="VoidStockOutcome"/>: <c>StockReturned</c>/<c>WrittenOff</c> dahil) yalnız <c>Voided</c> iken dolu;
/// <see cref="LateEntryReason"/> yalnız <see cref="AddedAfterFinalization"/> iken dolu.
/// <see cref="VaccinationId"/> dolu ise uygulama bir aşı kaydı ile birlikte yazılmıştır.
/// <see cref="PackageChargeSuggestionId"/> dolu ise uygulama fiyatlı tedavi paketine dahildir; ücreti paket kalemindedir (EXAM-007).</summary>
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
    DateOnly? ExpiryDate = null,
    Guid? VaccinationId = null,
    string? Route = null,
    string? Site = null,
    string? ProductManufacturer = null,
    AdministrationBillingDto? Billing = null,
    Guid? PackageChargeSuggestionId = null);

/// <summary>Düşümden sonraki stok durumu; düşük stok yalnız bilgidir, kaydı engellemez. <see cref="IsBelowMinimum"/> kullanılabilir miktara bakar.</summary>
public sealed record AdministrationStockDto(
    decimal QuantityOnHand,
    decimal MinimumStockLevel,
    bool IsBelowMinimum,
    decimal AvailableQuantity,
    decimal ExpiredQuantity);

/// <summary>Oluşturma sonucu; <see cref="Created"/> false ise aynı <c>requestId</c> ile tekrar istek ilk kaydı döndürmüştür.</summary>
public sealed record AdministrationCreateResultDto(bool Created, AdministrationDto Administration, AdministrationStockDto Stock);

/// <summary>
/// Uygulama satırının ücret durumu (RECORD-001; yalnız <c>Charges.Read</c> ile dolu). <see cref="Status"/>: <c>None</c> (ücret kalemi doğmadı),
/// <c>Pending</c> (ücret bekliyor), <c>Charged</c> (ücrete çevrildi), <c>ChargeVoided</c> (ücret sonradan iptal), <c>Waived</c> (ücretsiz sayıldı),
/// <c>Cancelled</c> (uygulama iptaliyle düştü). <see cref="SuggestedAmount"/> = katalog fiyatı × miktar; <see cref="ChargeAmount"/> kesilen ücrettir.
/// </summary>
public sealed record AdministrationBillingDto(
    string Status,
    decimal? SuggestedUnitPrice = null,
    decimal? SuggestedAmount = null,
    Guid? ChargeId = null,
    decimal? ChargeAmount = null,
    string? WaiveReason = null);
