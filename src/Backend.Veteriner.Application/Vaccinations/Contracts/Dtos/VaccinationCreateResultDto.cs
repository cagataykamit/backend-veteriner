using Backend.Veteriner.Application.Administrations.Contracts.Dtos;

namespace Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;

/// <summary>
/// Aşı kaydı oluşturma sonucu. <see cref="Administration"/> ve <see cref="Stock"/> yalnız istekte ürün verildiyse doludur;
/// <see cref="Created"/> false ise aynı <c>requestId</c> ile tekrar istek ilk kaydı döndürmüştür.
/// </summary>
public sealed record VaccinationCreateResultDto(
    Guid Id,
    bool Created,
    AdministrationDto? Administration,
    AdministrationStockDto? Stock);
