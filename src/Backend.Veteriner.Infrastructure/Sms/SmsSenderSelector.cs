using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Sms;

/// <summary>
/// <c>Sms:Provider</c> değerine göre sağlayıcıyı seçer (<see cref="SmsOptions.Provider"/>). Sahte sağlayıcı üretimde kabul edilmez;
/// boş değer Development ve IntegrationTests dışında yapılandırılmamış demektir.
/// </summary>
public static class SmsSenderSelector
{
    public const string IntegrationTestsEnvironment = "IntegrationTests";

    public static ISmsSender Select(IServiceProvider sp)
    {
        var provider = sp.GetRequiredService<IOptions<SmsOptions>>().Value.Provider?.Trim();
        var environment = sp.GetRequiredService<IHostEnvironment>();

        if (string.IsNullOrEmpty(provider)
            && (environment.IsDevelopment() || environment.IsEnvironment(IntegrationTestsEnvironment)))
        {
            provider = SmsOptions.LogProvider;
        }

        if (string.Equals(provider, SmsOptions.NetgsmProvider, StringComparison.OrdinalIgnoreCase))
            return sp.GetRequiredService<NetgsmSmsSender>();

        if (string.Equals(provider, SmsOptions.LogProvider, StringComparison.OrdinalIgnoreCase))
            return sp.GetRequiredService<LogOnlySmsSender>();

        return new UnconfiguredSmsSender();
    }
}
