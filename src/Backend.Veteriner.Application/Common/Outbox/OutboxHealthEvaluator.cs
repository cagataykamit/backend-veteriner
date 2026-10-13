using Backend.Veteriner.Application.Common.Options;

namespace Backend.Veteriner.Application.Common.Outbox;

/// <summary>Outbox health kurallarını tek yerde toplar (test edilebilir, hosting-neutral).</summary>
public static class OutboxHealthEvaluator
{
    public static OutboxHealthEvaluation Evaluate(OutboxStatus status, OutboxHealthOptions options)
    {
        var data = new Dictionary<string, object?>
        {
            ["pendingCount"] = status.PendingCount,
            ["deadLetterCount"] = status.DeadLetterCount,
            ["oldestPendingAgeSeconds"] = status.OldestPendingAge?.TotalSeconds ?? 0d
        };

        if (status.DeadLetterCount > 0 && options.DeadLetterIsUnhealthy)
            return new(OutboxHealthLevel.Unhealthy,
                $"Dead-letter count: {status.DeadLetterCount}, pending: {status.PendingCount}", data);

        if (status.PendingCount > 0 && status.OldestPendingAge is { } age)
        {
            var ageSeconds = age.TotalSeconds;
            if (ageSeconds >= options.UnhealthyAfterSeconds)
                return new(OutboxHealthLevel.Unhealthy, $"Oldest pending outbox message age {ageSeconds:F0}s.", data);

            if (ageSeconds >= options.DegradedAfterSeconds)
                return new(OutboxHealthLevel.Degraded, $"Oldest pending outbox message age {ageSeconds:F0}s.", data);
        }

        if (status.PendingCount > options.PendingDegradedThreshold)
            return new(OutboxHealthLevel.Degraded, $"Pending too high: {status.PendingCount}", data);

        return new(OutboxHealthLevel.Healthy,
            $"Pending: {status.PendingCount}, dead: {status.DeadLetterCount}", data);
    }
}
