namespace Backend.Veteriner.Api.Configuration;

/// <summary>
/// Zorunlu gizli/ortama özgü ayarlar eksikse uygulama başlamaz. Development, Production ve Staging için geçerlidir
/// (test ortamları kendi yapılandırmasını getirir). Hata iletileri yalnız ayar adını söyler, değer yazdırmaz.
/// </summary>
public static class StartupConfigurationValidator
{
    private const int JwtKeyMinLength = 32;
    private const string SmtpPlaceholderHost = "smtp.example.com";

    public static void EnsureRequiredSettings(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsProduction() && !environment.IsStaging())
            return;

        var source = environment.IsDevelopment()
            ? "User-Secrets üzerinden"
            : "ortam değişkeni veya gizli depo üzerinden";

        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
            throw Missing("Jwt:Key", source);
        if (jwtKey.Length < JwtKeyMinLength)
            throw new InvalidOperationException($"Jwt:Key en az {JwtKeyMinLength} karakter olmalıdır.");

        Require(configuration.GetConnectionString("DefaultConnection"), "ConnectionStrings:DefaultConnection", source);
        Require(configuration.GetConnectionString("QueryConnection"), "ConnectionStrings:QueryConnection", source);
        Require(configuration["Smtp:Pass"], "Smtp:Pass", source);

        if (!environment.IsDevelopment()
            && string.Equals(configuration["Smtp:Host"], SmtpPlaceholderHost, StringComparison.OrdinalIgnoreCase))
        {
            throw Missing("Smtp:Host", source);
        }
    }

    private static void Require(string? value, string name, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Missing(name, source);
    }

    private static InvalidOperationException Missing(string name, string source)
        => new($"{name} boş veya geçersiz. {source} set edilmelidir.");
}
