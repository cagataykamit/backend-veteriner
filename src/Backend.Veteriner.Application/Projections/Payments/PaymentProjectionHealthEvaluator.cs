using Backend.Veteriner.Application.Common.Options;

namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Payment finance projection health kurallarını tek yerde toplar (test edilebilir, hosting-neutral).
/// <see cref="PaymentProjectionOptions.Enabled"/> default <c>false</c> olduğundan projection kapalıyken
/// kuyruk lag/dead-letter production'ı gereksiz unhealthy yapmaz; sinyaller yine <c>data</c> alanında okunur.
/// </summary>
public static class PaymentProjectionHealthEvaluator
{
    public static PaymentProjectionHealthEvaluation Evaluate(
        PaymentProjectionStatus status,
        PaymentProjectionHealthOptions healthOptions)
    {
        var data = BuildData(status);

        if (!status.QueryDatabaseReachable)
            return Unhealthy("Query SQL Server bağlantısı başarısız.", data);

        if (status.QueryDatabaseHasPendingMigrations)
            return Unhealthy("Query DB bekleyen migration var.", data);

        if (!status.ProjectionEnabled)
            return Healthy("Payment projection disabled; queue signals exposed for pre-rollout observability.", data);

        if (status.DeadLetterCount > 0 && healthOptions.DeadLetterIsUnhealthy)
            return Unhealthy($"Payment projection dead-letter count: {status.DeadLetterCount}.", data);

        if (status.PendingCount > 0 && status.OldestPendingAge is { } age)
        {
            var ageSeconds = age.TotalSeconds;
            if (ageSeconds >= healthOptions.UnhealthyAfterSeconds)
                return Unhealthy($"Oldest pending payment event age {ageSeconds:F0}s.", data);

            if (ageSeconds >= healthOptions.DegradedAfterSeconds)
                return Degraded($"Oldest pending payment event age {ageSeconds:F0}s.", data);
        }

        if (status.RetryWaitingCount > 0)
            return Degraded($"Payment projection retry-waiting count: {status.RetryWaitingCount}.", data);

        return Healthy("Payment projection queue healthy.", data);
    }

    private static IReadOnlyDictionary<string, object?> BuildData(PaymentProjectionStatus status)
        => new Dictionary<string, object?>
        {
            ["pendingCount"] = status.PendingCount,
            ["retryWaitingCount"] = status.RetryWaitingCount,
            ["deadLetterCount"] = status.DeadLetterCount,
            ["oldestPendingAgeSeconds"] = status.OldestPendingAge?.TotalSeconds ?? 0d,
            ["nextRetryAtUtc"] = status.NextRetryAtUtc?.ToString("O") ?? string.Empty,
            ["projectionEnabled"] = status.ProjectionEnabled
        };

    private static PaymentProjectionHealthEvaluation Healthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(PaymentProjectionHealthLevel.Healthy, description, data);

    private static PaymentProjectionHealthEvaluation Degraded(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(PaymentProjectionHealthLevel.Degraded, description, data);

    private static PaymentProjectionHealthEvaluation Unhealthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(PaymentProjectionHealthLevel.Unhealthy, description, data);
}
