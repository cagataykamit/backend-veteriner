namespace Backend.Veteriner.Application.Projections.Pets;

/// <summary>
/// Pet read-model backfill için saf, deterministik karar mantığı.
/// DB erişimi yoktur; <see cref="Backend.Veteriner.Infrastructure.Projections.Pets.PetReadModelBackfillService"/>
/// bu kararları uygular.
///
/// Tasarım kararları:
/// - Pet domain'inde <c>CreatedAtUtc</c> / <c>UpdatedAtUtc</c> yoktur; backfill bir <em>event</em> değil
///   bir <em>snapshot</em>'tır. Ordering anahtarı için minimum UTC sentinel kullanılır
///   (<see cref="BackfillBaselineOccurredAtUtc"/>). Gerçek <c>pet.created.v1</c> /
///   <c>pet.updated.v1</c> event'leri handler'da <c>DateTime.UtcNow</c> ile gelir ve sentinel'i ezer.
/// - Stale guard <see cref="Backend.Veteriner.Infrastructure.Projections.Pets.PetProjectionProcessor"/>
///   ile aynıdır: daha eski snapshot, daha yeni bir event ile yazılmış satırı ezmez.
/// </summary>
public static class PetReadModelBackfillPlanner
{
    /// <summary>
    /// Backfill snapshot ordering anahtarı. Wall-clock kullanılmaz; gerçek event'ler her zaman daha yenidir.
    /// </summary>
    public static DateTime BackfillBaselineOccurredAtUtc { get; } =
        DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    /// <summary>
    /// Backfill snapshot'ı için stale-guard ordering anahtarını üretir.
    /// </summary>
    public static DateTime ResolveOccurredAtUtc() => BackfillBaselineOccurredAtUtc;

    /// <summary>
    /// Mevcut read-model satırının ordering değerine göre alınacak kararı verir.
    /// </summary>
    public static PetReadModelBackfillAction Decide(
        DateTime backfillOccurredAtUtc,
        DateTime? existingLastEventOccurredAtUtc)
    {
        if (existingLastEventOccurredAtUtc is null)
            return PetReadModelBackfillAction.Insert;

        return backfillOccurredAtUtc < existingLastEventOccurredAtUtc.Value
            ? PetReadModelBackfillAction.SkipStale
            : PetReadModelBackfillAction.Update;
    }
}
