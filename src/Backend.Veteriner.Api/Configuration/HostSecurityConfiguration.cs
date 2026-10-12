namespace Backend.Veteriner.Api.Configuration;

/// <summary>
/// CORS origin ve Host süzmesi ayarları. Development/test dışında güvenli varsayılan yoktur: ayar eksikse başlangıçta hata verilir.
/// </summary>
public static class HostSecurityConfiguration
{
    private const string DevelopmentOrigin = "http://localhost:4200";

    public static string[] ResolveAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var origins = configuration.GetSection("AllowedOrigins").Get<string[]>()?
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToArray();

        if (origins is { Length: > 0 })
            return origins;

        if (RequiresExplicitHostSecurity(environment))
            throw new InvalidOperationException(
                "AllowedOrigins ayarı boş. Üretim/staging için izinli frontend origin'leri (AllowedOrigins:0, AllowedOrigins:1 ...) tanımlanmalıdır.");

        return [DevelopmentOrigin];
    }

    public static void EnsureAllowedHostsRestricted(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!RequiresExplicitHostSecurity(environment))
            return;

        var hosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("*"))
            throw new InvalidOperationException(
                "AllowedHosts üretim/staging için '*' veya boş olamaz. Alan adlarını ';' ile ayırarak tanımlayın.");
    }

    private static bool RequiresExplicitHostSecurity(IHostEnvironment environment)
        => environment.IsProduction() || environment.IsStaging();
}
