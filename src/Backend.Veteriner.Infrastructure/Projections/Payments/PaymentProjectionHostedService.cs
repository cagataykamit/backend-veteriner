using Backend.Veteriner.Application.Projections.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Payment finance projection processor'ı periyodik olarak tetikleyen background worker.
/// </summary>
public sealed class PaymentProjectionHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly PaymentProjectionOptions _options;
    private readonly ILogger<PaymentProjectionHostedService> _logger;

    public PaymentProjectionHostedService(
        IServiceProvider sp,
        IOptions<PaymentProjectionOptions> options,
        ILogger<PaymentProjectionHostedService> logger)
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
                "PaymentProjection disabled via {Section}:Enabled=false; background polling skipped.",
                PaymentProjectionOptions.SectionName);
            return;
        }

        var idleInterval = TimeSpan.FromSeconds(Math.Max(1, _options.LoopIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;

            try
            {
                using var scope = _sp.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IPaymentProjectionProcessor>();
                processedCount = await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Payment projection tick failed.");
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
