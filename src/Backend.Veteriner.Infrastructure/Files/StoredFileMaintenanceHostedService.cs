using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Files;

/// <summary>
/// Dosya bakım işçisi: açılıştan kısa süre sonra ilk tur, sonra <see cref="StoredFileMaintenanceOptions.IntervalMinutes"/> aralıkla
/// <see cref="StoredFileMaintenanceProcessor"/> çalışır; tur hatası işçiyi durdurmaz (içe aktarma bakım işçisi deseni).
/// </summary>
public sealed class StoredFileMaintenanceHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly StoredFileMaintenanceProcessor _processor;
    private readonly StoredFileMaintenanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StoredFileMaintenanceHostedService> _logger;

    public StoredFileMaintenanceHostedService(
        StoredFileMaintenanceProcessor processor,
        IOptions<StoredFileMaintenanceOptions> options,
        TimeProvider timeProvider,
        ILogger<StoredFileMaintenanceHostedService> logger)
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
            _logger.LogInformation("Stored file maintenance worker disabled via FileStorage:Maintenance:Enabled=false.");
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
                _logger.LogError("Stored file maintenance tick failed. Exception={ExceptionType}", ex.GetType().FullName);
            }

            delay = interval;
        }
    }
}
