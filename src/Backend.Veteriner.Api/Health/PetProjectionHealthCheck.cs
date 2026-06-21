using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Health;

public sealed class PetProjectionHealthCheck : IHealthCheck
{
    private readonly IPetProjectionStatusReader _statusReader;
    private readonly PetProjectionHealthOptions _healthOptions;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly PetProjectionOptions _projectionOptions;
    private readonly ILogger<PetProjectionHealthCheck> _logger;

    public PetProjectionHealthCheck(
        IPetProjectionStatusReader statusReader,
        IOptions<PetProjectionHealthOptions> healthOptions,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        IOptions<PetProjectionOptions> projectionOptions,
        ILogger<PetProjectionHealthCheck> logger)
    {
        _statusReader = statusReader;
        _healthOptions = healthOptions.Value;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _projectionOptions = projectionOptions.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _statusReader.GetStatusAsync(cancellationToken);

            var evaluation = PetProjectionHealthEvaluator.Evaluate(
                status,
                _healthOptions,
                _queryReadModelsOptions);

            LogHealthLevel(evaluation);

            var data = new Dictionary<string, object?>(evaluation.Data)
            {
                ["claimingEnabled"] = _projectionOptions.ClaimingEnabled
            };

            return MapToHealthCheckResult(evaluation, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PetProjectionHealthUnhealthy Reason=health_check_exception");
            return HealthCheckResult.Unhealthy("Pet projection health check failed.", ex);
        }
    }

    private void LogHealthLevel(PetProjectionHealthEvaluation evaluation)
    {
        var data = evaluation.Data;
        var pendingCount = Convert.ToInt32(data.GetValueOrDefault("pendingCount") ?? 0);
        var retryWaitingCount = Convert.ToInt32(data.GetValueOrDefault("retryWaitingCount") ?? 0);
        var deadLetterCount = Convert.ToInt32(data.GetValueOrDefault("deadLetterCount") ?? 0);
        var oldestPendingAgeSeconds = Convert.ToDouble(data.GetValueOrDefault("oldestPendingAgeSeconds") ?? 0d);

        switch (evaluation.Level)
        {
            case PetProjectionHealthLevel.Degraded:
                _logger.LogWarning(
                    "PetProjectionHealthDegraded PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount} DeadLetterCount={DeadLetterCount} OldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                    pendingCount,
                    retryWaitingCount,
                    deadLetterCount,
                    oldestPendingAgeSeconds);
                break;
            case PetProjectionHealthLevel.Unhealthy:
                _logger.LogError(
                    "PetProjectionHealthUnhealthy PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount} DeadLetterCount={DeadLetterCount} OldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                    pendingCount,
                    retryWaitingCount,
                    deadLetterCount,
                    oldestPendingAgeSeconds);
                break;
        }
    }

    internal static HealthCheckResult MapToHealthCheckResult(
        PetProjectionHealthEvaluation evaluation,
        IReadOnlyDictionary<string, object?> data)
    {
        var status = evaluation.Level switch
        {
            PetProjectionHealthLevel.Healthy => HealthStatus.Healthy,
            PetProjectionHealthLevel.Degraded => HealthStatus.Degraded,
            _ => HealthStatus.Unhealthy
        };

        return new HealthCheckResult(
            status,
            evaluation.Description,
            data: data
                .Where(kvp => kvp.Value is not null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!));
    }
}
