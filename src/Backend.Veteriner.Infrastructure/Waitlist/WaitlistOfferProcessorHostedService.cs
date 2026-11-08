using Backend.Veteriner.Application.Waitlist;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Waitlist;

public sealed class WaitlistOfferProcessorOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 60;
}

/// <summary>
/// Bekleme listesi işleyicisini (TIER-018, <see cref="WaitlistOfferProcessor"/>) aralıkla çalıştırır: süresi dolan teklifleri kapatır,
/// boşalan saati sıradakine teklif eder. Her tur kendi kapsamında çalışır.
/// </summary>
public sealed class WaitlistOfferProcessorHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly WaitlistOfferProcessorOptions _options;
    private readonly ILogger<WaitlistOfferProcessorHostedService> _logger;

    public WaitlistOfferProcessorHostedService(
        IServiceProvider sp,
        IOptions<WaitlistOfferProcessorOptions> options,
        ILogger<WaitlistOfferProcessorHostedService> logger)
    {
        _sp = sp;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("WaitlistOfferProcessor disabled via Waitlist:Processor:Enabled=false.");
            return;
        }

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, _options.IntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _sp.CreateScope();
                await scope.ServiceProvider.GetRequiredService<WaitlistOfferProcessor>().ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Waitlist offer processor tick failed.");
            }
        }
    }
}
