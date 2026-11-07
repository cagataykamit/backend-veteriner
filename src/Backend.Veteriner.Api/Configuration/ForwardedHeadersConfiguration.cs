using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Backend.Veteriner.Api.Configuration;

/// <summary>
/// Vekil başlıkları (<c>X-Forwarded-For</c>, <c>X-Forwarded-Proto</c>). Çerçevenin varsayılanı yalnız loopback'e güvenir; başka vekil
/// ancak <see cref="KnownProxiesKey"/> ile açıkça eklenir. Hız sınırı istemci IP'sine bağlı olduğundan site uygulaması (SSR) ya da ters
/// vekil başka makinedeyse adresi buraya yazılır (docs/CLINIC_SITE_API_CONTRACT.md §9). Geçersiz adres açılışta hata verir.
/// </summary>
public static class ForwardedHeadersConfiguration
{
    public const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";

    public static void Apply(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.RequireHeaderSymmetry = false;
        options.ForwardLimit = null;

        foreach (var value in configuration.GetSection(KnownProxiesKey).Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException($"{KnownProxiesKey} geçerli bir IP adresi içermiyor: '{value}'.");

            options.KnownProxies.Add(address);
        }
    }
}
