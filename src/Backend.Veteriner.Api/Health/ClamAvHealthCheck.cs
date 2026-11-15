using Backend.Veteriner.Infrastructure.Files;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Backend.Veteriner.Api.Health;

/// <summary>
/// clamd <c>PING</c> (yalnız <c>FileScanning:Enabled</c> açıkken kayıtlı). Ulaşılamazsa kaydın hata durumu (Degraded) döner:
/// API çalışır ama tarama açıkken yüklemeler reddedilir. <c>/health/ready</c> Degraded'i 503 sayar.
/// </summary>
public sealed class ClamAvHealthCheck : IHealthCheck
{
    private readonly ClamAvFileScanner _scanner;

    public ClamAvHealthCheck(ClamAvFileScanner scanner) => _scanner = scanner;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var failure = await _scanner.PingAsync(cancellationToken);
        return failure is null
            ? HealthCheckResult.Healthy("clamd yanıt veriyor.")
            : new HealthCheckResult(context.Registration.FailureStatus, $"clamd yanıt vermiyor; dosya yüklemeleri reddedilir. {failure}");
    }
}
