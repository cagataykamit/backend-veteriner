using Backend.Veteriner.Application.Imports.Commit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// İçe aktarma uygulama işçisi (ADR-019 K11): istek/sekme kapansa da iş sürer. İş varsa ara vermeden sıradakini alır,
/// yoksa <see cref="ImportCommitOptions.PollIntervalSeconds"/> bekler. Kapanışta yarım iş kira dolunca devralınır.
/// </summary>
public sealed class ImportCommitHostedService : BackgroundService
{
    private readonly ImportCommitProcessor _processor;
    private readonly ImportCommitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportCommitHostedService> _logger;

    public ImportCommitHostedService(
        ImportCommitProcessor processor,
        IOptions<ImportCommitOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportCommitHostedService> logger)
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
            _logger.LogInformation("Import commit worker disabled via Imports:Commit:WorkerEnabled=false.");
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
                _logger.LogError("Import commit worker tick failed. Exception={ExceptionType}", ex.GetType().FullName);
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
