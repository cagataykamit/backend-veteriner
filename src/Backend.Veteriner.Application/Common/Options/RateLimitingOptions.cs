using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Backend.Veteriner.Application.Common.Options;

/// <summary>
/// API global rate limiter yapılandırması (named policy'ler ayrı sabitlenir).
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public const int DefaultGlobalPermitLimit = 200;
    public const int MinGlobalPermitLimit = 1;
    public const int MaxGlobalPermitLimit = 100_000;

    /// <summary>Global sliding-window <c>PermitLimit</c> (IP başına, 1 dakika pencere).</summary>
    public int GlobalPermitLimit { get; set; } = DefaultGlobalPermitLimit;

    public int GetEffectiveGlobalPermitLimit()
        => ResolveEffectiveGlobalPermitLimit(GlobalPermitLimit);

    public static int ResolveEffectiveGlobalPermitLimit(int configured)
    {
        if (configured < MinGlobalPermitLimit || configured > MaxGlobalPermitLimit)
            return DefaultGlobalPermitLimit;

        return configured;
    }

    public static RateLimitingOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new RateLimitingOptions();
        var raw = configuration.GetSection(SectionName)["GlobalPermitLimit"];
        if (raw is not null
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            options.GlobalPermitLimit = parsed;
        }

        return options;
    }
}
