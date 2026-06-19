namespace Backend.Veteriner.Application.Clients.ReadModels;

/// <summary>
/// Command DB Clients ile Query DB ClientReadModels satır sayısı parity okuması (operasyonel gözlem).
/// </summary>
public interface IClientReadModelParityReader
{
    /// <summary>Tüm tenant'lar için toplam parity.</summary>
    Task<ClientReadModelParityResult> GetGlobalParityAsync(CancellationToken cancellationToken = default);

    /// <summary>Tek tenant kapsamında parity (tenant izolasyon doğrulaması için).</summary>
    Task<ClientReadModelParityResult> GetTenantParityAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
