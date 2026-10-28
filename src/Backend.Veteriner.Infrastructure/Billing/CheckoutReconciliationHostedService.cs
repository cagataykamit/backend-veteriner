using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace Backend.Veteriner.Infrastructure.Billing;

/// <summary><see cref="CheckoutReconciliationProcessor"/> turlarını aralıklı çalıştırır.</summary>
public sealed class CheckoutReconciliationHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly CheckoutReconciliationOptions _opt;

    public CheckoutReconciliationHostedService(IServiceProvider sp, IOptions<CheckoutReconciliationOptions> opt)
    {
        _sp = sp;
        _opt = opt.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
            return;

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(30, _opt.IntervalSeconds)));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _sp.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<CheckoutReconciliationProcessor>();
                await processor.RunOnceAsync(stoppingToken);
                await processor.RefundPendingOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Checkout reconciliation failed.");
            }
        }
    }
}
