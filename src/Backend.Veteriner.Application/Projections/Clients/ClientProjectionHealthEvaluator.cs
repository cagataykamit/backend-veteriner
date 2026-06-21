using Backend.Veteriner.Application.Common.Options;

namespace Backend.Veteriner.Application.Projections.Clients;

/// <summary>
/// Client projection health kurallarını tek yerde toplar (test edilebilir, hosting-neutral).
/// Appointment health evaluator deseniyle hizalıdır; read flag yalnızca <see cref="QueryReadModelsOptions.ClientsEnabled"/>'dır.
/// Claim/lease opt-in: <see cref="ClientProjectionOptions.ClaimingEnabled"/> (health data: claimingEnabled).
/// </summary>
public static class ClientProjectionHealthEvaluator
{
    public static ClientProjectionHealthEvaluation Evaluate(
        ClientProjectionStatus status,
        ClientProjectionHealthOptions healthOptions,
        QueryReadModelsOptions queryReadModelsOptions)
    {
        var data = BuildData(status, queryReadModelsOptions);

        if (!status.QueryDatabaseReachable)
            return Unhealthy("Query SQL Server bağlantısı başarısız.", data);

        if (status.QueryDatabaseHasPendingMigrations)
            return Unhealthy("Query DB bekleyen migration var.", data);

        if (status.DeadLetterCount > 0 && healthOptions.DeadLetterIsUnhealthy)
            return Unhealthy($"Client projection dead-letter count: {status.DeadLetterCount}.", data);

        if (!status.ProjectionEnabled && queryReadModelsOptions.ClientsEnabled)
        {
            if (status.PendingCount > 0 || status.RetryWaitingCount > 0)
                return Unhealthy("Query read flag enabled but client projection disabled with pending client events.", data);

            return Degraded("Query read flag enabled but client projection disabled.", data);
        }

        if (status.PendingCount > 0 && status.OldestPendingAge is { } age)
        {
            var ageSeconds = age.TotalSeconds;
            if (ageSeconds >= healthOptions.UnhealthyAfterSeconds)
                return Unhealthy($"Oldest pending client event age {ageSeconds:F0}s.", data);

            if (ageSeconds >= healthOptions.DegradedAfterSeconds)
                return Degraded($"Oldest pending client event age {ageSeconds:F0}s.", data);
        }

        if (status.RetryWaitingCount > 0)
            return Degraded($"Client projection retry-waiting count: {status.RetryWaitingCount}.", data);

        return Healthy("Client projection queue healthy.", data);
    }

    private static IReadOnlyDictionary<string, object?> BuildData(
        ClientProjectionStatus status,
        QueryReadModelsOptions queryReadModelsOptions)
        => new Dictionary<string, object?>
        {
            ["pendingCount"] = status.PendingCount,
            ["retryWaitingCount"] = status.RetryWaitingCount,
            ["deadLetterCount"] = status.DeadLetterCount,
            ["oldestPendingAgeSeconds"] = status.OldestPendingAge?.TotalSeconds ?? 0d,
            ["nextRetryAtUtc"] = status.NextRetryAtUtc?.ToString("O") ?? string.Empty,
            ["projectionEnabled"] = status.ProjectionEnabled,
            ["clientsReadEnabled"] = queryReadModelsOptions.ClientsEnabled
        };

    private static ClientProjectionHealthEvaluation Healthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(ClientProjectionHealthLevel.Healthy, description, data);

    private static ClientProjectionHealthEvaluation Degraded(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(ClientProjectionHealthLevel.Degraded, description, data);

    private static ClientProjectionHealthEvaluation Unhealthy(
        string description,
        IReadOnlyDictionary<string, object?> data)
        => new(ClientProjectionHealthLevel.Unhealthy, description, data);
}
