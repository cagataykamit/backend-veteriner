using Backend.Veteriner.Infrastructure.Files;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Health;

/// <summary>
/// Virüs taraması açıkken clamd <c>PING</c>. Ulaşılamazsa kaydın hata durumu (Degraded) döner: API çalışır ama yüklemeler
/// reddedilir; <c>/health/ready</c> Degraded'i 503 sayar. Tarama kapalıysa Healthy döner ve bunu açıklamada söyler.
/// </summary>
public sealed class ClamAvHealthCheck : IHealthCheck
{
    private readonly ClamAvFileScanner _scanner;
    private readonly FileScanningOptions _options;

    public ClamAvHealthCheck(ClamAvFileScanner scanner, IOptions<FileScanningOptions> options)
    {
        _scanner = scanner;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return HealthCheckResult.Healthy("Virüs taraması kapalı (FileScanning:Enabled=false); yüklemeler taranmıyor.");

        var failure = await _scanner.PingAsync(cancellationToken);
        return failure is null
            ? HealthCheckResult.Healthy("clamd yanıt veriyor.")
            : new HealthCheckResult(context.Registration.FailureStatus, $"clamd yanıt vermiyor; dosya yüklemeleri reddedilir. {failure}");
    }
}
