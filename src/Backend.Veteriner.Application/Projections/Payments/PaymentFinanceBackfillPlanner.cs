namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Payment finance backfill için saf, deterministik karar mantığı.
/// DB erişimi yoktur; <see cref="Backend.Veteriner.Infrastructure.Projections.Payments.PaymentFinanceBackfillService"/>
/// bu kararları uygular.
///
/// Payment domain'inde mutasyon timestamp yok → minimum UTC sentinel kullanılır
/// (<see cref="BackfillBaselineOccurredAtUtc"/>). Gerçek integration event'leri handler'da
/// <c>DateTime.UtcNow</c> ile gelir ve sentinel'i ezer.
/// </summary>
public static class PaymentFinanceBackfillPlanner
{
    public static DateTime BackfillBaselineOccurredAtUtc { get; } =
        DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    public static DateTime ResolveOccurredAtUtc() => BackfillBaselineOccurredAtUtc;

    public static PaymentFinanceBackfillAction Decide(
        DateTime backfillOccurredAtUtc,
        DateTime? existingLastEventOccurredAtUtc)
    {
        if (existingLastEventOccurredAtUtc is null)
            return PaymentFinanceBackfillAction.Insert;

        return backfillOccurredAtUtc < existingLastEventOccurredAtUtc.Value
            ? PaymentFinanceBackfillAction.SkipStale
            : PaymentFinanceBackfillAction.Update;
    }
}
