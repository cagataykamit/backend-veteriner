using Backend.Veteriner.Application.Imports.Commit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// İçe aktarma doğrulama işçisi (ADR-019 K13): istek/sekme kapansa da doğrulama sürer. Uygulama işçisinin ayarlarını
/// (<see cref="ImportCommitOptions.WorkerEnabled"/>, <see cref="ImportCommitOptions.PollIntervalSeconds"/>) kullanır; iş
/// varsa ara vermeden sıradakini alır. Kapanışta yarım iş kira dolunca devralınır.
/// </summary>
public sealed class ImportValidationHostedService : BackgroundService
{
    private readonly ImportValidationProcessor _processor;
    private readonly ImportCommitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportValidationHostedService> _logger;

    public ImportValidationHostedService(
        ImportValidationProcessor processor,
        IOptions<ImportCommitOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportValidationHostedService> logger)
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
            _logger.LogInformation("Import validation worker disabled via Imports:Commit:WorkerEnabled=false.");
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
                _logger.LogError("Import validation worker tick failed. Exception={ExceptionType}", ex.GetType().FullName);
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
