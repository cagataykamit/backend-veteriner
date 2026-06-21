using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Health;

public sealed class ClientProjectionHealthCheck : IHealthCheck
{
    private readonly IClientProjectionStatusReader _statusReader;
    private readonly ClientProjectionHealthOptions _healthOptions;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ClientProjectionOptions _projectionOptions;
    private readonly ILogger<ClientProjectionHealthCheck> _logger;

    public ClientProjectionHealthCheck(
        IClientProjectionStatusReader statusReader,
        IOptions<ClientProjectionHealthOptions> healthOptions,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        IOptions<ClientProjectionOptions> projectionOptions,
        ILogger<ClientProjectionHealthCheck> logger)
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

            var evaluation = ClientProjectionHealthEvaluator.Evaluate(
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
            _logger.LogError(ex, "ClientProjectionHealthUnhealthy Reason=health_check_exception");
            return HealthCheckResult.Unhealthy("Client projection health check failed.", ex);
        }
    }

    private void LogHealthLevel(ClientProjectionHealthEvaluation evaluation)
    {
        var data = evaluation.Data;
        var pendingCount = Convert.ToInt32(data.GetValueOrDefault("pendingCount") ?? 0);
        var retryWaitingCount = Convert.ToInt32(data.GetValueOrDefault("retryWaitingCount") ?? 0);
        var deadLetterCount = Convert.ToInt32(data.GetValueOrDefault("deadLetterCount") ?? 0);
        var oldestPendingAgeSeconds = Convert.ToDouble(data.GetValueOrDefault("oldestPendingAgeSeconds") ?? 0d);

        switch (evaluation.Level)
        {
            case ClientProjectionHealthLevel.Degraded:
                _logger.LogWarning(
                    "ClientProjectionHealthDegraded PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount} DeadLetterCount={DeadLetterCount} OldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                    pendingCount,
                    retryWaitingCount,
                    deadLetterCount,
                    oldestPendingAgeSeconds);
                break;
            case ClientProjectionHealthLevel.Unhealthy:
                _logger.LogError(
                    "ClientProjectionHealthUnhealthy PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount} DeadLetterCount={DeadLetterCount} OldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                    pendingCount,
                    retryWaitingCount,
                    deadLetterCount,
                    oldestPendingAgeSeconds);
                break;
        }
    }

    internal static HealthCheckResult MapToHealthCheckResult(
        ClientProjectionHealthEvaluation evaluation,
        IReadOnlyDictionary<string, object?> data)
    {
        var status = evaluation.Level switch
        {
            ClientProjectionHealthLevel.Healthy => HealthStatus.Healthy,
            ClientProjectionHealthLevel.Degraded => HealthStatus.Degraded,
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
