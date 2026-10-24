using Backend.Veteriner.Application.Tenants.Contracts.Dtos;

namespace Backend.Veteriner.Application.Tenants.ExampleClinic;

/// <summary>
/// Örnek Veteriner Kliniği sentetik verisini (ADR-002) verilen kliniğe yazar. Yalnız veri üretir; "kiracı deneme
/// sürecinde mi", "klinik boş mu" gibi iş kuralları çağıran komutta kalır (<c>LoadExampleClinicCommandHandler</c>,
/// <c>PublicOwnerSignupCommandHandler</c>). Çağıranın işlem (transaction) sınırında çalışır.
/// </summary>
public interface IExampleClinicLoader
{
    /// <param name="userId">Kayıtlarda "oluşturan/kesinleştiren" olarak görünen kiracı yöneticisi.</param>
    Task<ExampleClinicLoadSummaryDto> LoadAsync(Guid tenantId, Guid clinicId, Guid userId, CancellationToken ct);
}
