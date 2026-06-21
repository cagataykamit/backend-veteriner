namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Payment list read-model backfill (CQRS-14F) için saf, deterministik karar mantığı.
/// DB erişimi yoktur; <see cref="Backend.Veteriner.Infrastructure.Projections.Payments.PaymentReadModelBackfillService"/>
/// bu kararları uygular.
///
/// Tasarım kararları (finance backfill ile birebir hizalı):
/// - Payment domain'inde mutasyon timestamp'i yoktur; backfill bir <em>event</em> değil bir
///   <em>snapshot</em>'tır. Ordering anahtarı için minimum UTC sentinel kullanılır
///   (<see cref="BackfillBaselineOccurredAtUtc"/>). Gerçek <c>payment.created.v1</c> /
///   <c>payment.updated.v1</c> event'leri handler'da <c>DateTime.UtcNow</c> ile gelir ve sentinel'i ezer.
/// - Stale guard <see cref="Backend.Veteriner.Infrastructure.Projections.Payments.PaymentProjectionProcessor"/>
///   ile aynıdır: daha eski snapshot, daha yeni bir event ile yazılmış satırı ezmez.
/// </summary>
public static class PaymentReadModelBackfillPlanner
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
    public static PaymentReadModelBackfillAction Decide(
        DateTime backfillOccurredAtUtc,
        DateTime? existingLastEventOccurredAtUtc)
    {
        if (existingLastEventOccurredAtUtc is null)
            return PaymentReadModelBackfillAction.Insert;

        return backfillOccurredAtUtc < existingLastEventOccurredAtUtc.Value
            ? PaymentReadModelBackfillAction.SkipStale
            : PaymentReadModelBackfillAction.Update;
    }
}
