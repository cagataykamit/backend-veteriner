namespace Backend.Veteriner.Application.Reports.Registers.VaccineInfo;

/// <summary>
/// Aşı bilgi raporu satırı (bir aşı uygulaması). <c>ClientIdentityNumber</c> eksik alandır, her zaman null.
/// <c>UnitPrice</c>/<c>TotalPrice</c> ürünün <b>güncel katalog fiyatı</b>dır (uygulama bir ücrete bağlı değildir).
/// Seri no ve SKT uygulamanın partisinden gelir; partisiz bakiyeden düşülen uygulamada null'dır.
/// </summary>
public sealed record VaccineInfoRegisterItemDto(
    int RowNumber,
    Guid AdministrationId,
    DateTime AdministeredAtUtc,
    string OwnerName,
    string? ClientIdentityNumber,
    string PetName,
    string Species,
    string? MicrochipNumber,
    string ProductName,
    string? LotNumber,
    DateOnly? ExpiryDate,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    string Currency,
    bool AddedAfterFinalization,
    bool IsVoided);
