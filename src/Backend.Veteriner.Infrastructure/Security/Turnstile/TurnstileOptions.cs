namespace Backend.Veteriner.Infrastructure.Security.Turnstile;

/// <summary>
/// Cloudflare Turnstile ayarları (<c>Turnstile</c>). Gizli anahtar depoya yazılmaz: geliştirmede user-secrets, sunucuda ortam
/// değişkeni (<c>Turnstile__SecretKey</c>). Site anahtarı herkese açıktır; istemci <c>GET /public/owner-signup/settings</c> ile okur.
/// </summary>
public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public string? SiteKey { get; init; }

    public string? SecretKey { get; init; }

    /// <summary>developers.cloudflare.com/turnstile/get-started/server-side-validation (2026-11-15 okundu).</summary>
    public string VerifyUrl { get; init; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    /// <summary>
    /// Yanıttaki <c>hostname</c> bu alan adı ya da alt alan adı olmalı (ör. <c>vetinity.com</c>); boşsa denetlenmez (yerel deneme).
    /// </summary>
    public string? ExpectedHostname { get; init; }

    public int TimeoutSeconds { get; init; } = 5;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SiteKey)
        && !string.IsNullOrWhiteSpace(SecretKey)
        && Uri.TryCreate(VerifyUrl, UriKind.Absolute, out _);
}
