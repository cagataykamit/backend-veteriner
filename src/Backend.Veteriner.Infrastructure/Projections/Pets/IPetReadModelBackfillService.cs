namespace Backend.Veteriner.Infrastructure.Projections.Pets;

/// <summary>
/// Command DB <c>Pets</c> kayıtlarını Query DB <c>PetReadModels</c> tablosuna idempotent
/// biçimde dolduran/yeniden oluşturan backfill servisi (CQRS-12C-6).
/// </summary>
public interface IPetReadModelBackfillService
{
    /// <summary>
    /// Backfill çalıştırır.
    /// </summary>
    /// <param name="tenantId">
    /// Verilirse yalnızca o tenant'ın pet'leri işlenir (tenant izolasyonu). <c>null</c> ise
    /// tüm tenant'lar.
    /// </param>
    /// <param name="batchSize">Command DB'den tek seferde okunacak satır sayısı.</param>
    Task<PetReadModelBackfillResult> BackfillAsync(
        Guid? tenantId = null,
        int batchSize = PetReadModelBackfillService.DefaultBatchSize,
        CancellationToken cancellationToken = default);
}
