using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// İçe aktarma bakım işçisi (ADR-019 K9): süresi dolan taslaklar ve 30 günlük satır verisi temizliği. Açılıştan kısa süre
/// sonra ilk tur, sonra <see cref="ImportMaintenanceOptions.IntervalMinutes"/> aralıkla çalışır; tur hatası işçiyi durdurmaz.
/// </summary>
public sealed class ImportMaintenanceHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly ImportMaintenanceProcessor _processor;
    private readonly ImportMaintenanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportMaintenanceHostedService> _logger;

    public ImportMaintenanceHostedService(
        ImportMaintenanceProcessor processor,
        IOptions<ImportMaintenanceOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportMaintenanceHostedService> logger)
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
            _logger.LogInformation("Import maintenance worker disabled via Imports:Maintenance:Enabled=false.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        var delay = StartupDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await _processor.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Import maintenance tick failed. Exception={ExceptionType}", ex.GetType().FullName);
            }

            delay = interval;
        }
    }
}
