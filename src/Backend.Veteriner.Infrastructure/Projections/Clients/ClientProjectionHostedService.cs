using Backend.Veteriner.Application.Projections.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Clients;

/// <summary>
/// Client projection processor'ı periyodik olarak tetikleyen background worker.
/// Appointment hosted service deseniyle hizalıdır; dolu batch sonrası hemen tekrar dener (drain),
/// boş batch'te idle interval kadar bekler.
/// </summary>
public sealed class ClientProjectionHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ClientProjectionOptions _options;
    private readonly ILogger<ClientProjectionHostedService> _logger;

    public ClientProjectionHostedService(
        IServiceProvider sp,
        IOptions<ClientProjectionOptions> options,
        ILogger<ClientProjectionHostedService> logger)
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
                "ClientProjection disabled via {Section}:Enabled=false; background polling skipped.",
                ClientProjectionOptions.SectionName);
            return;
        }

        var idleInterval = TimeSpan.FromSeconds(Math.Max(1, _options.LoopIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;

            try
            {
                using var scope = _sp.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();
                processedCount = await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Client projection tick failed.");
            }

            // Dolu batch sonrası beklemeden sıradakini dene; aksi halde idle bekle.
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
