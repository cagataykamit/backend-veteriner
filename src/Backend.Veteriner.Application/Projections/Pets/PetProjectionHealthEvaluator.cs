using Backend.Veteriner.Application.Common.Options;

namespace Backend.Veteriner.Application.Projections.Pets;

/// <summary>
/// Pet projection health kurallarını tek yerde toplar (test edilebilir, hosting-neutral).
/// Client projection health evaluator deseniyle hizalıdır; read flag yalnızca
/// <see cref="QueryReadModelsOptions.PetsEnabled"/>'dır.
/// </summary>
public static class PetProjectionHealthEvaluator
{
    public static PetProjectionHealthEvaluation Evaluate(
        PetProjectionStatus status,
        PetProjectionHealthOptions healthOptions,
        QueryReadModelsOptions queryReadModelsOptions)
    {
        var data = BuildData(status, queryReadModelsOptions);

        if (!status.QueryDatabaseReachable)
            return Unhealthy("Query SQL Server bağlantısı başarısız.", data);

        if (status.QueryDatabaseHasPendingMigrations)
            return Unhealthy("Query DB bekleyen migration var.", data);

        if (status.DeadLetterCount > 0 && healthOptions.DeadLetterIsUnhealthy)
            return Unhealthy($"Pet projection dead-letter count: {status.DeadLetterCount}.", data);

        if (!status.ProjectionEnabled && queryReadModelsOptions.PetsEnabled)
        {
            if (status.PendingCount > 0 || status.RetryWaitingCount > 0)
                return Unhealthy("Query read flag enabled but pet projection disabled with pending pet events.", data);

            return Degraded("Query read flag enabled but pet projection disabled.", data);
        }

        if (status.PendingCount > 0 && status.OldestPendingAge is { } age)
        {
            var ageSeconds = age.TotalSeconds;
            if (ageSeconds >= healthOptions.UnhealthyAfterSeconds)
                return Unhealthy($"Oldest pending pet event age {ageSeconds:F0}s.", data);

            if (ageSeconds >= healthOptions.DegradedAfterSeconds)
                return Degraded($"Oldest pending pet event age {ageSeconds:F0}s.", data);
        }

        if (status.RetryWaitingCount > 0)
            return Degraded($"Pet projection retry-waiting count: {status.RetryWaitingCount}.", data);

        return Healthy("Pet projection queue healthy.", data);
    }

    private static IReadOnlyDictionary<string, object?> BuildData(
        PetProjectionStatus status,
        QueryReadModelsOptions queryReadModelsOptions)
        => new Dictionary<string, object?>
        {
            ["pendingCount"] = status.PendingCount,
            ["retryWaitingCount"] = status.RetryWaitingCount,
            ["deadLetterCount"] = status.DeadLetterCount,
            ["oldestPendingAgeSeconds"] = status.OldestPendingAge?.TotalSeconds ?? 0d,
            ["nextRetryAtUtc"] = status.NextRetryAtUtc?.ToString("O") ?? string.Empty,
            ["projectionEnabled"] = status.ProjectionEnabled,
            ["petsReadEnabled"] = queryReadModelsOptions.PetsEnabled
        };

    private static PetProjectionHealthEvaluation Healthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(PetProjectionHealthLevel.Healthy, description, data);

    private static PetProjectionHealthEvaluation Degraded(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(PetProjectionHealthLevel.Degraded, description, data);

    private static PetProjectionHealthEvaluation Unhealthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(PetProjectionHealthLevel.Unhealthy, description, data);
}
