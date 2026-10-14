using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Pets.Access;

/// <summary>
/// Hasta kartı bölüm durumu kuralı (özet ve açık işler paylaşır): izin yoksa <c>Forbidden</c> (sorgu yapılmaz),
/// erişilebilir klinik yoksa <c>Empty</c>, kayıt yoksa <c>Empty</c>, sorgu hata verirse <c>Unavailable</c> (kısmi yanıt).
/// </summary>
internal static class PetSectionLoader
{
    public static async Task<PetSummarySectionDto<T>> LoadAsync<T>(
        bool canRead,
        bool noClinicAccess,
        Func<Task<T?>> load,
        ILogger logger,
        string section,
        CancellationToken ct) where T : class
    {
        if (!canRead)
            return PetSummarySectionDto<T>.Forbidden();

        if (noClinicAccess)
            return PetSummarySectionDto<T>.Empty();

        try
        {
            var data = await load();
            return data is null ? PetSummarySectionDto<T>.Empty() : PetSummarySectionDto<T>.Ok(data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Hasta kartı bölümü okunamadı: {Section}", section);
            return PetSummarySectionDto<T>.Unavailable();
        }
    }
}
