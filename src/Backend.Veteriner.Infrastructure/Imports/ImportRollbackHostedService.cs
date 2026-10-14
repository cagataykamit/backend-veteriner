using Backend.Veteriner.Application.Imports.Commit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// İçe aktarma geri alma işçisi (ADR-019 K7/K11): istek/sekme kapansa da iş sürer. Uygulama işçisiyle aynı ayarları
/// (<see cref="ImportCommitOptions"/>) ve döngüyü kullanır; kapanışta yarım iş kira dolunca devralınır.
/// </summary>
public sealed class ImportRollbackHostedService : BackgroundService
{
    private readonly ImportRollbackProcessor _processor;
    private readonly ImportCommitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportRollbackHostedService> _logger;

    public ImportRollbackHostedService(
        ImportRollbackProcessor processor,
        IOptions<ImportCommitOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportRollbackHostedService> logger)
    {
        _processor = processor;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled)
        {
            _logger.LogInformation("Import rollback worker disabled via Imports:Commit:WorkerEnabled=false.");
            return;
        }

        var idle = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                worked = await _processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Import rollback worker tick failed. Exception={ExceptionType}", ex.GetType().FullName);
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
