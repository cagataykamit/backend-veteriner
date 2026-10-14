namespace Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;

/// <summary>Aşı kaydına bağlı uygulamanın (stok düşümü) özeti; ürün bilgisi uygulama anındaki parti ve miktardır.</summary>
public sealed record VaccinationAdministrationDto(
    Guid AdministrationId,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    string Unit,
    Guid? BatchId,
    string? LotNumber,
    DateOnly? ExpiryDate);
