namespace Backend.Veteriner.Infrastructure.Projections.Clients;

/// <summary>
/// Command DB <c>Clients</c> kayıtlarını Query DB <c>ClientReadModels</c> tablosuna idempotent
/// biçimde dolduran/yeniden oluşturan backfill servisi (CQRS-12B-6).
/// </summary>
public interface IClientReadModelBackfillService
{
    /// <summary>
    /// Backfill çalıştırır.
    /// </summary>
    /// <param name="tenantId">
    /// Verilirse yalnızca o tenant'ın client'ları işlenir (tenant izolasyonu). <c>null</c> ise
    /// tüm tenant'lar.
    /// </param>
    /// <param name="batchSize">Command DB'den tek seferde okunacak satır sayısı.</param>
    Task<ClientReadModelBackfillResult> BackfillAsync(
        Guid? tenantId = null,
        int batchSize = ClientReadModelBackfillService.DefaultBatchSize,
        CancellationToken cancellationToken = default);
}
