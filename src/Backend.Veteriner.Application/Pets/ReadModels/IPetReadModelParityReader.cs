namespace Backend.Veteriner.Application.Pets.ReadModels;

/// <summary>
/// Command DB Pets ile Query DB PetReadModels satır sayısı parity okuması (operasyonel gözlem).
/// </summary>
public interface IPetReadModelParityReader
{
    /// <summary>Tüm tenant'lar için toplam parity.</summary>
    Task<PetReadModelParityResult> GetGlobalParityAsync(CancellationToken cancellationToken = default);

    /// <summary>Tek tenant kapsamında parity (tenant izolasyon doğrulaması için).</summary>
    Task<PetReadModelParityResult> GetTenantParityAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
