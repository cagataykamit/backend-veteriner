using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Health;

public sealed class PaymentProjectionHealthCheck : IHealthCheck
{
    private readonly IPaymentProjectionStatusReader _statusReader;
    private readonly IPaymentReadModelHealthReader _readModelHealthReader;
    private readonly PaymentProjectionHealthOptions _healthOptions;
    private readonly PaymentProjectionOptions _projectionOptions;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ILogger<PaymentProjectionHealthCheck> _logger;

    public PaymentProjectionHealthCheck(
        IPaymentProjectionStatusReader statusReader,
        IPaymentReadModelHealthReader readModelHealthReader,
        IOptions<PaymentProjectionHealthOptions> healthOptions,
        IOptions<PaymentProjectionOptions> projectionOptions,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        ILogger<PaymentProjectionHealthCheck> logger)
    {
        _statusReader = statusReader;
        _readModelHealthReader = readModelHealthReader;
        _healthOptions = healthOptions.Value;
        _projectionOptions = projectionOptions.Value;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _statusReader.GetStatusAsync(cancellationToken);

            // Read-model drift sinyali yalnızca gate açıkken hesaplanır (production default kapalı → ekstra sorgu yok).
            PaymentReadModelHealthSignal? readModelSignal = null;
            if (status.QueryDatabaseReachable
                && !status.QueryDatabaseHasPendingMigrations
                && (_projectionOptions.Enabled || _queryReadModelsOptions.PaymentsListReadEnabled))
            {
                readModelSignal = await _readModelHealthReader.GetSignalAsync(cancellationToken);
            }

            var evaluation = PaymentProjectionHealthEvaluator.Evaluate(status, _healthOptions, readModelSignal);

            LogHealthLevel(evaluation);

            var data = new Dictionary<string, object?>(evaluation.Data)
            {
                ["claimingEnabled"] = _projectionOptions.ClaimingEnabled
            };

            return MapToHealthCheckResult(evaluation, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PaymentProjectionHealthUnhealthy Reason=health_check_exception");
            return HealthCheckResult.Unhealthy("Payment projection health check failed.", ex);
        }
    }

    private void LogHealthLevel(PaymentProjectionHealthEvaluation evaluation)
    {
        var data = evaluation.Data;
        var pendingCount = Convert.ToInt32(data.GetValueOrDefault("pendingCount") ?? 0);
        var retryWaitingCount = Convert.ToInt32(data.GetValueOrDefault("retryWaitingCount") ?? 0);
        var deadLetterCount = Convert.ToInt32(data.GetValueOrDefault("deadLetterCount") ?? 0);
        var oldestPendingAgeSeconds = Convert.ToDouble(data.GetValueOrDefault("oldestPendingAgeSeconds") ?? 0d);

        switch (evaluation.Level)
        {
            case PaymentProjectionHealthLevel.Degraded:
                _logger.LogWarning(
                    "PaymentProjectionHealthDegraded PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount} DeadLetterCount={DeadLetterCount} OldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                    pendingCount,
                    retryWaitingCount,
                    deadLetterCount,
                    oldestPendingAgeSeconds);
                break;
            case PaymentProjectionHealthLevel.Unhealthy:
                _logger.LogError(
                    "PaymentProjectionHealthUnhealthy PendingCount={PendingCount} RetryWaitingCount={RetryWaitingCount} DeadLetterCount={DeadLetterCount} OldestPendingAgeSeconds={OldestPendingAgeSeconds}",
                    pendingCount,
                    retryWaitingCount,
                    deadLetterCount,
                    oldestPendingAgeSeconds);
                break;
        }
    }

    internal static HealthCheckResult MapToHealthCheckResult(
        PaymentProjectionHealthEvaluation evaluation,
        IReadOnlyDictionary<string, object?> data)
    {
        var status = evaluation.Level switch
        {
            PaymentProjectionHealthLevel.Healthy => HealthStatus.Healthy,
            PaymentProjectionHealthLevel.Degraded => HealthStatus.Degraded,
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
