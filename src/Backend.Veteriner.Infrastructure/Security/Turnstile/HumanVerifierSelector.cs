using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Security.Turnstile;

/// <summary>
/// Anahtarlar (<c>Turnstile:SiteKey</c> ve <c>Turnstile:SecretKey</c>) girilmişse gerçek Turnstile; girilmemişse doğrulama atlanır
/// (her ortamda; site anahtarı boş döner, istemci widget çizmez). Anahtar girilince başka değişiklik olmadan devreye girer; hız
/// sınırları ve e-posta kodu kuralları her durumda çalışır.
/// </summary>
public static class HumanVerifierSelector
{
    public static IHumanVerifier Select(IServiceProvider sp)
        => sp.GetRequiredService<IOptions<TurnstileOptions>>().Value.IsConfigured
            ? sp.GetRequiredService<TurnstileHumanVerifier>()
            : new PassThroughHumanVerifier();
}

/// <summary>Başlangıçta bir kez: anahtarlar girilmemişse insan doğrulamasının atlandığını günlüğe yazar.</summary>
public sealed class TurnstileConfigurationReporter : IHostedService
{
    private readonly IOptions<TurnstileOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<TurnstileConfigurationReporter> _logger;

    public TurnstileConfigurationReporter(
        IOptions<TurnstileOptions> options,
        IHostEnvironment environment,
        ILogger<TurnstileConfigurationReporter> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.IsConfigured)
        {
            _logger.LogInformation("Cloudflare Turnstile insan doğrulaması etkin.");
        }
        else if (_environment.IsProduction())
        {
            _logger.LogWarning(
                "Cloudflare Turnstile anahtarları (Turnstile:SiteKey, Turnstile:SecretKey) girilmemiş: insan doğrulaması atlanıyor; " +
                "yalnız hız sınırları ve e-posta kodu kuralları çalışıyor.");
        }
        else
        {
            _logger.LogInformation("Cloudflare Turnstile anahtarları girilmemiş: insan doğrulaması atlanıyor ({Environment}).", _environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
