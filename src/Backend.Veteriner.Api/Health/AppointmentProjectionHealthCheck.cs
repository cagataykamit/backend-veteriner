using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Health;

public sealed class AppointmentProjectionHealthCheck : IHealthCheck
{
    private readonly IAppointmentProjectionStatusReader _statusReader;
    private readonly AppointmentProjectionHealthOptions _healthOptions;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly AppointmentProjectionMetricsSnapshotHolder _snapshotHolder;
    private readonly AppointmentProjectionOptions _projectionOptions;
    private readonly ILogger<AppointmentProjectionHealthCheck> _logger;

    public AppointmentProjectionHealthCheck(
        IAppointmentProjectionStatusReader statusReader,
        IOptions<AppointmentProjectionHealthOptions> healthOptions,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        IOptions<AppointmentProjectionOptions> projectionOptions,
        AppointmentProjectionMetricsSnapshotHolder snapshotHolder,
        ILogger<AppointmentProjectionHealthCheck> logger)
    {
        _statusReader = statusReader;
        _healthOptions = healthOptions.Value;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _projectionOptions = projectionOptions.Value;
        _snapshotHolder = snapshotHolder;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _statusReader.GetStatusAsync(cancellationToken);
            _snapshotHolder.Update(
                AppointmentProjectionMetricsSnapshotFactory.Create(status, _queryReadModelsOptions));

            var evaluation = AppointmentProjectionHealthEvaluator.Evaluate(
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
            _logger.LogError(ex, "AppointmentProjectionHealthUnhealthy Reason=health_check_exception");
            return HealthCheckResult.Unhealthy("Appointment projection health check failed.", ex);
        }
    }

    private void LogHealthLevel(AppointmentProjectionHealthEvaluation evaluation)
    {
        var data = evaluation.Data;
        var pendingCount = Convert.ToInt32(data.GetValueOrDefault("pendingCount") ?? 0);
        var retryWaitingCount = Convert.ToInt32(data.GetValueOrDefault("retryWaitingCount") ?? 0);
        var deadLetterCount = Convert.ToInt32(data.GetValueOrDefault("deadLetterCount") ?? 0);
        var oldestPendingAgeSeconds = Convert.ToDouble(data.GetValueOrDefault("oldestPendingAgeSeconds") ?? 0d);

        switch (evaluation.Level)
        {
            case AppointmentProjectionHealthLevel.Degraded:
                _logger.LogWarning(
                    "AppointmentProjectionHealthDegraded PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount} DeadLetterCount={DeadLetterCount} OldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                    pendingCount,
                    retryWaitingCount,
                    deadLetterCount,
                    oldestPendingAgeSeconds);
                break;
            case AppointmentProjectionHealthLevel.Unhealthy:
                _logger.LogError(
                    "AppointmentProjectionHealthUnhealthy PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount} DeadLetterCount={DeadLetterCount} OldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                    pendingCount,
                    retryWaitingCount,
                    deadLetterCount,
                    oldestPendingAgeSeconds);
                break;
            case AppointmentProjectionHealthLevel.Healthy when deadLetterCount == 0 && pendingCount == 0 && retryWaitingCount == 0:
                _logger.LogDebug(
                    "AppointmentProjectionRecovered PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount}",
                    pendingCount,
                    retryWaitingCount);
                break;
        }
    }

    internal static HealthCheckResult MapToHealthCheckResult(
        AppointmentProjectionHealthEvaluation evaluation,
        IReadOnlyDictionary<string, object?>? dataOverride = null)
    {
        var status = evaluation.Level switch
        {
            AppointmentProjectionHealthLevel.Healthy => HealthStatus.Healthy,
            AppointmentProjectionHealthLevel.Degraded => HealthStatus.Degraded,
            _ => HealthStatus.Unhealthy
        };

        var sourceData = dataOverride ?? evaluation.Data;

        return new HealthCheckResult(
            status,
            evaluation.Description,
            data: sourceData
                .Where(kvp => kvp.Value is not null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!));
    }
}
