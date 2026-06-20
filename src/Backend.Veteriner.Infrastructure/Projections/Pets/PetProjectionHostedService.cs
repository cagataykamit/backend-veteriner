using Backend.Veteriner.Application.Projections.Pets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Pets;

/// <summary>
/// Pet projection processor'ı periyodik olarak tetikleyen background worker.
/// Client hosted service deseniyle hizalıdır; dolu batch sonrası hemen tekrar dener (drain),
/// boş batch'te idle interval kadar bekler.
/// </summary>
public sealed class PetProjectionHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly PetProjectionOptions _options;
    private readonly ILogger<PetProjectionHostedService> _logger;

    public PetProjectionHostedService(
        IServiceProvider sp,
        IOptions<PetProjectionOptions> options,
        ILogger<PetProjectionHostedService> logger)
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
                "PetProjection disabled via {Section}:Enabled=false; background polling skipped.",
                PetProjectionOptions.SectionName);
            return;
        }

        var idleInterval = TimeSpan.FromSeconds(Math.Max(1, _options.LoopIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;

            try
            {
                using var scope = _sp.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();
                processedCount = await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pet projection tick failed.");
            }

            if (processedCount > 0)
                continue;

            try
            {
                await Task.Delay(idleInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
