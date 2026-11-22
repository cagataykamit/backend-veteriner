using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Backend.Veteriner.Application.Common.Options;

/// <summary>
/// API global rate limiter yapılandırması (named policy'ler ayrı sabitlenir). Giriş yapmış istek kullanıcıya göre
/// (<see cref="UserPermitLimit"/>), girişsiz istek istemci IP'sine göre (<see cref="GlobalPermitLimit"/>) bölünür: bir klinikte bütün
/// personel aynı dış IP'den gelir, IP başına tek sınır ekibi birlikte kilitler (tam test 2026-11-22).
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public const int DefaultGlobalPermitLimit = 200;
    public const int DefaultUserPermitLimit = 600;
    public const int MinGlobalPermitLimit = 1;
    public const int MaxGlobalPermitLimit = 100_000;

    /// <summary>False iken API pipeline rate limiter middleware'i kaydedilmez (IntegrationTests).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Girişsiz istekte global sliding-window <c>PermitLimit</c> (IP başına, 1 dakika pencere).</summary>
    public int GlobalPermitLimit { get; set; } = DefaultGlobalPermitLimit;

    /// <summary>Giriş yapmış istekte global sliding-window <c>PermitLimit</c> (kullanıcı başına, 1 dakika pencere).</summary>
    public int UserPermitLimit { get; set; } = DefaultUserPermitLimit;

    public int GetEffectiveGlobalPermitLimit()
        => ResolveEffectiveGlobalPermitLimit(GlobalPermitLimit);

    public int GetEffectiveUserPermitLimit()
        => Resolve(UserPermitLimit, DefaultUserPermitLimit);

    public static int ResolveEffectiveGlobalPermitLimit(int configured)
        => Resolve(configured, DefaultGlobalPermitLimit);

    private static int Resolve(int configured, int fallback)
        => configured is < MinGlobalPermitLimit or > MaxGlobalPermitLimit ? fallback : configured;

    public static RateLimitingOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new RateLimitingOptions();
        var section = configuration.GetSection(SectionName);
        if (TryReadInt(section["GlobalPermitLimit"], out var global))
            options.GlobalPermitLimit = global;
        if (TryReadInt(section["UserPermitLimit"], out var user))
            options.UserPermitLimit = user;

        if (bool.TryParse(section["Enabled"], out var enabled))
            options.Enabled = enabled;

        return options;
    }

    private static bool TryReadInt(string? raw, out int value)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
