namespace Backend.Veteriner.Infrastructure.Ai.Anthropic;

/// <summary>
/// Anthropic Messages API ayarları (<c>Ai:Anthropic</c>). Anahtar yalnız backend'de: user-secrets ya da ortam değişkeni
/// (<c>Ai__Anthropic__ApiKey</c>); appsettings dosyasına yazılmaz. Anahtar yoksa AI "yapılandırılmamış" sayılır.
/// </summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Ai:Anthropic";

    public string? ApiKey { get; init; }

    public string ApiBaseUrl { get; init; } = "https://api.anthropic.com";

    /// <summary><c>anthropic-version</c> başlığı.</summary>
    public string ApiVersion { get; init; } = "2026-10-21";

    /// <summary>Düşünme derinliği (<c>output_config.effort</c>: low, medium, high); özet için düşük yeterli varsayıldı, ölçülmedi.</summary>
    public string Effort { get; init; } = "low";

    public int TimeoutSeconds { get; init; } = 60;

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(ApiKey) && Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out _);
}
