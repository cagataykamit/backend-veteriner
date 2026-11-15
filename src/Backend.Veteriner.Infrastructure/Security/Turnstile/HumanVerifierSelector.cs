using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Security.Turnstile;

/// <summary>
/// Anahtarlar girilmişse gerçek Turnstile; girilmemişse Development ve IntegrationTests'te sahte (her isteği geçirir), diğer
/// ortamlarda yapılandırılmamış (korunan uçlar 503 ile durur, doğrulamasız devam edilmez).
/// </summary>
public static class HumanVerifierSelector
{
    public const string IntegrationTestsEnvironment = "IntegrationTests";

    public static IHumanVerifier Select(IServiceProvider sp)
    {
        if (sp.GetRequiredService<IOptions<TurnstileOptions>>().Value.IsConfigured)
            return sp.GetRequiredService<TurnstileHumanVerifier>();

        var environment = sp.GetRequiredService<IHostEnvironment>();
        return environment.IsDevelopment() || environment.IsEnvironment(IntegrationTestsEnvironment)
            ? new PassThroughHumanVerifier()
            : new UnconfiguredHumanVerifier();
    }
}
