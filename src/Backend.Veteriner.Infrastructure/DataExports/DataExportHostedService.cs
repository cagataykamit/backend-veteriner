using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.DataExports;

/// <summary>
/// Tüm veri dışa aktarma işçisi: iş varsa ara vermeden sıradakini alır, yoksa <see cref="DataExportWorkerOptions.PollIntervalSeconds"/>
/// bekler; <see cref="DataExportWorkerOptions.MaintenanceIntervalMinutes"/> aralıkla süresi dolan paketleri siler. Tur hatası işçiyi
/// durdurmaz; kapanışta yarım iş kira dolunca devralınır.
/// </summary>
public sealed class DataExportHostedService : BackgroundService
{
    private readonly DataExportProcessor _processor;
    private readonly DataExportWorkerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DataExportHostedService> _logger;

    public DataExportHostedService(
        DataExportProcessor processor,
        IOptions<DataExportWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<DataExportHostedService> logger)
    {
        _processor = processor;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Data export worker disabled via DataExports:Worker:Enabled=false.");
            return;
        }

        var idle = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var maintenanceEvery = TimeSpan.FromMinutes(Math.Max(1, _options.MaintenanceIntervalMinutes));
        var nextMaintenance = _timeProvider.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                if (_timeProvider.GetUtcNow() >= nextMaintenance)
                {
                    // Önce zaman ilerler: bakım hatası iş işlemeyi her turda engellemesin.
                    nextMaintenance = _timeProvider.GetUtcNow() + maintenanceEvery;
                    await _processor.RunMaintenanceAsync(stoppingToken);
                }

                worked = await _processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Data export worker tick failed. Exception={ExceptionType}", ex.GetType().FullName);
            }

            if (worked)
                continue;

            try
            {
                await Task.Delay(idle, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
