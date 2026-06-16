using Backend.Veteriner.Application.Appointments.IntegrationEvents;
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

    public AppointmentProjectionHostedService(
        IServiceProvider sp,
        IOptions<AppointmentProjectionOptions> options,
        ILogger<AppointmentProjectionHostedService> logger)
    {
        _sp = sp;
        _options = options.Value;
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

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.LoopIntervalSeconds));
        var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _sp.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionProcessor>();
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Appointment projection tick failed.");
            }
        }
    }
}
