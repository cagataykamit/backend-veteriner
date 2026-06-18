using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Appointments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly AppointmentProjectionOptions _options;
    private readonly ILogger<AppointmentProjectionHostedService> _logger;
    private readonly TimeProvider _timeProvider;

    public AppointmentProjectionHostedService(
        IServiceProvider sp,
        IOptions<AppointmentProjectionOptions> options,
        TimeProvider timeProvider,
        ILogger<AppointmentProjectionHostedService> logger)
    {
        _sp = sp;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "AppointmentProjection disabled via {Section}:Enabled=false; background polling skipped.",
                AppointmentProjectionOptions.SectionName);
            return;
        }

        DateTimeOffset? lastActivityUtc = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;

            try
            {
                using var scope = _sp.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();
                processedCount = await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Appointment projection tick failed.");
            }

            if (processedCount > 0)
                lastActivityUtc = _timeProvider.GetUtcNow();

            if (processedCount > 0)
            {
                try
                {
                    using var refreshScope = _sp.CreateScope();
                    await AppointmentProjectionMetricsStatusRefresher.RefreshAsync(
                        refreshScope.ServiceProvider,
                        stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Appointment projection metrics snapshot refresh failed.");
                }
            }

            if (!AppointmentProjectionPollingLoop.ShouldIdleWaitAfterBatch(processedCount))
                continue;

            var idleDelay = AppointmentProjectionPollingLoop.ResolveIdleDelay(
                processedCount,
                lastActivityUtc,
                _timeProvider.GetUtcNow(),
                _options.LoopIntervalSeconds,
                _options.ActiveFollowUpWindowSeconds,
                _options.ActiveFollowUpPollMilliseconds);

            if (idleDelay <= TimeSpan.Zero)
                continue;

            try
            {
                await Task.Delay(idleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
