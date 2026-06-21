using Backend.Veteriner.Application.Common.Options;

namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Payment finance projection health kurallarını tek yerde toplar (test edilebilir, hosting-neutral).
/// <see cref="PaymentProjectionOptions.Enabled"/> default <c>false</c> olduğundan projection kapalıyken
/// kuyruk lag/dead-letter production'ı gereksiz unhealthy yapmaz; sinyaller yine <c>data</c> alanında okunur.
///
/// CQRS-14F: opsiyonel <see cref="PaymentReadModelHealthSignal"/> ile payment list read-model (PaymentReadModels)
/// count drift sinyali eklenir. Bu sinyal finance kuyruk değerlendirmesinden bağımsızdır; nihai seviye iki boyutun
/// <em>en kötüsüdür</em>. Sinyal <c>null</c> iken (veya gate kapalıyken) davranış 13D ile birebir aynıdır.
/// </summary>
public static class PaymentProjectionHealthEvaluator
{
    public static PaymentProjectionHealthEvaluation Evaluate(
        PaymentProjectionStatus status,
        PaymentProjectionHealthOptions healthOptions,
        PaymentReadModelHealthSignal? readModelSignal = null)
    {
        var data = BuildData(status, readModelSignal);

        if (!status.QueryDatabaseReachable)
            return Unhealthy("Query SQL Server bağlantısı başarısız.", data);

        if (status.QueryDatabaseHasPendingMigrations)
            return Unhealthy("Query DB bekleyen migration var.", data);

        var queue = EvaluateQueue(status, healthOptions);
        var readModel = EvaluateReadModelDrift(readModelSignal, status.ProjectionEnabled);

        // En kötü severity kazanır; eşitlikte kuyruk açıklaması korunur (13D davranışı değişmez).
        if (readModel is { } rm && rm.Level > queue.Level)
            return new PaymentProjectionHealthEvaluation(rm.Level, rm.Description, data);

        return new PaymentProjectionHealthEvaluation(queue.Level, queue.Description, data);
    }

    /// <summary>13D ile birebir aynı finance kuyruk değerlendirmesi.</summary>
    private static (PaymentProjectionHealthLevel Level, string Description) EvaluateQueue(
        PaymentProjectionStatus status,
        PaymentProjectionHealthOptions healthOptions)
    {
        if (!status.ProjectionEnabled)
            return (PaymentProjectionHealthLevel.Healthy,
                "Payment projection disabled; queue signals exposed for pre-rollout observability.");

        if (status.DeadLetterCount > 0 && healthOptions.DeadLetterIsUnhealthy)
            return (PaymentProjectionHealthLevel.Unhealthy,
                $"Payment projection dead-letter count: {status.DeadLetterCount}.");

        if (status.PendingCount > 0 && status.OldestPendingAge is { } age)
        {
            var ageSeconds = age.TotalSeconds;
            if (ageSeconds >= healthOptions.UnhealthyAfterSeconds)
                return (PaymentProjectionHealthLevel.Unhealthy, $"Oldest pending payment event age {ageSeconds:F0}s.");

            if (ageSeconds >= healthOptions.DegradedAfterSeconds)
                return (PaymentProjectionHealthLevel.Degraded, $"Oldest pending payment event age {ageSeconds:F0}s.");
        }

        if (status.RetryWaitingCount > 0)
            return (PaymentProjectionHealthLevel.Degraded,
                $"Payment projection retry-waiting count: {status.RetryWaitingCount}.");

        return (PaymentProjectionHealthLevel.Healthy, "Payment projection queue healthy.");
    }

    /// <summary>
    /// Payment list read-model count drift değerlendirmesi (CQRS-14F).
    /// Gate: projection da list read flag'i de kapalıysa hiç değerlendirilmez (boş read-model production-safe).
    /// Drift varsa: list read flag'i açıksa Unhealthy (kullanıcı yanlış/eksik veri görür), yalnızca projection
    /// açıkken Degraded (backfill/catch-up penceresi).
    /// </summary>
    private static (PaymentProjectionHealthLevel Level, string Description)? EvaluateReadModelDrift(
        PaymentReadModelHealthSignal? signal,
        bool projectionEnabled)
    {
        if (signal is null)
            return null;

        if (!projectionEnabled && !signal.PaymentsListReadEnabled)
            return null;

        if (signal.CountInSync)
            return (PaymentProjectionHealthLevel.Healthy, "Payment list read-model count in sync.");

        if (signal.PaymentsListReadEnabled)
            return (PaymentProjectionHealthLevel.Unhealthy,
                $"Payment list read-model count drift {signal.AbsoluteCountDrift} while PaymentsListReadEnabled.");

        return (PaymentProjectionHealthLevel.Degraded,
            $"Payment list read-model count drift {signal.AbsoluteCountDrift}; backfill/catch-up pending.");
    }

    private static IReadOnlyDictionary<string, object?> BuildData(
        PaymentProjectionStatus status,
        PaymentReadModelHealthSignal? readModelSignal)
    {
        var data = new Dictionary<string, object?>
        {
            ["pendingCount"] = status.PendingCount,
            ["retryWaitingCount"] = status.RetryWaitingCount,
            ["deadLetterCount"] = status.DeadLetterCount,
            ["oldestPendingAgeSeconds"] = status.OldestPendingAge?.TotalSeconds ?? 0d,
            ["nextRetryAtUtc"] = status.NextRetryAtUtc?.ToString("O") ?? string.Empty,
            ["projectionEnabled"] = status.ProjectionEnabled
        };

        if (readModelSignal is { } signal)
        {
            data["paymentsListReadEnabled"] = signal.PaymentsListReadEnabled;
            data["readModelCommandPaymentCount"] = signal.CommandPaymentCount;
            data["readModelCount"] = signal.ReadModelCount;
            data["readModelCountDrift"] = signal.CountDrift;
            data["readModelCountInSync"] = signal.CountInSync;
        }

        return data;
    }

    private static PaymentProjectionHealthEvaluation Unhealthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(PaymentProjectionHealthLevel.Unhealthy, description, data);
}
