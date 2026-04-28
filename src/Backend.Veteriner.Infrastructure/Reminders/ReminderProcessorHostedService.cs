using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Reminders;

public sealed class ReminderProcessorHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ReminderProcessorOptions _options;
    private readonly ILogger<ReminderProcessorHostedService> _logger;

    public ReminderProcessorHostedService(
        IServiceProvider sp,
        IOptions<ReminderProcessorOptions> options,
        ILogger<ReminderProcessorHostedService> logger)
    {
        _sp = sp;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("ReminderProcessor disabled via Reminders:Processor:Enabled=false.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _sp.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ReminderProcessorService>();
                await processor.ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder processor tick failed.");
            }
        }
    }
}
