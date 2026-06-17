using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Appointments;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Health;

public sealed class AppointmentProjectionHealthCheck : IHealthCheck
{
    private readonly IAppointmentProjectionStatusReader _statusReader;
    private readonly AppointmentProjectionHealthOptions _healthOptions;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;

    public AppointmentProjectionHealthCheck(
        IAppointmentProjectionStatusReader statusReader,
        IOptions<AppointmentProjectionHealthOptions> healthOptions,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions)
    {
        _statusReader = statusReader;
        _healthOptions = healthOptions.Value;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _statusReader.GetStatusAsync(cancellationToken);
            var evaluation = AppointmentProjectionHealthEvaluator.Evaluate(
                status,
                _healthOptions,
                _queryReadModelsOptions);

            return MapToHealthCheckResult(evaluation);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Appointment projection health check failed.", ex);
        }
    }

    internal static HealthCheckResult MapToHealthCheckResult(AppointmentProjectionHealthEvaluation evaluation)
    {
        var status = evaluation.Level switch
        {
            AppointmentProjectionHealthLevel.Healthy => HealthStatus.Healthy,
            AppointmentProjectionHealthLevel.Degraded => HealthStatus.Degraded,
            _ => HealthStatus.Unhealthy
        };

        return new HealthCheckResult(
            status,
            evaluation.Description,
            data: evaluation.Data
                .Where(kvp => kvp.Value is not null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!));
    }
}
