namespace Backend.Veteriner.Application.Projections.Clients;

/// <summary>
/// Client read-model backfill için saf, deterministik karar mantığı.
/// DB erişimi yoktur; <see cref="ClientReadModelBackfillService"/> bu kararları uygular.
///
/// Tasarım kararları:
/// - Backfill bir <em>event</em> değil bir <em>snapshot</em>'tır; gerçek event zamanı yoktur.
///   Bu yüzden ordering anahtarı (<c>LastEventOccurredAtUtc</c>) için Command DB satırının
///   son mutasyon zamanı kullanılır: <see cref="ResolveOccurredAtUtc"/> =
///   <c>UpdatedAtUtc ?? CreatedAtUtc</c>. Bu değer deterministiktir (wall-clock'a bağlı değildir)
///   ve gerçek event'in <c>OccurredAtUtc</c>'sine en yakın güvenli yaklaşımdır.
/// - Stale guard <see cref="ClientProjectionProcessor"/> ile aynıdır: daha eski snapshot,
///   daha yeni bir event ile yazılmış satırı ezmez. Böylece backfill canlı projection akışıyla
///   çakışmadan idempotent biçimde çalışır.
/// </summary>
public static class ClientReadModelBackfillPlanner
{
    /// <summary>
    /// Backfill snapshot'ı için stale-guard ordering anahtarını üretir.
    /// </summary>
    public static DateTime ResolveOccurredAtUtc(DateTime createdAtUtc, DateTime? updatedAtUtc)
        => updatedAtUtc ?? createdAtUtc;

    /// <summary>
    /// Mevcut read-model satırının ordering değerine göre alınacak kararı verir.
    /// </summary>
    /// <param name="backfillOccurredAtUtc"><see cref="ResolveOccurredAtUtc"/> sonucu.</param>
    /// <param name="existingLastEventOccurredAtUtc">
    /// Query DB'de satır varsa onun <c>LastEventOccurredAtUtc</c> değeri; satır yoksa <c>null</c>.
    /// </param>
    public static ClientReadModelBackfillAction Decide(
        DateTime backfillOccurredAtUtc,
        DateTime? existingLastEventOccurredAtUtc)
    {
        if (existingLastEventOccurredAtUtc is null)
            return ClientReadModelBackfillAction.Insert;

        return backfillOccurredAtUtc < existingLastEventOccurredAtUtc.Value
            ? ClientReadModelBackfillAction.SkipStale
            : ClientReadModelBackfillAction.Update;
    }
}
