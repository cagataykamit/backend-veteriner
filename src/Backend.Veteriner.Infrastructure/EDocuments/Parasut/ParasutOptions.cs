using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Infrastructure.EDocuments.Parasut;

/// <summary>
/// Paraşüt uygulama ayarları (<c>EDocuments:Parasut</c>). <see cref="ClientId"/> ve <see cref="ClientSecret"/> yalnız
/// user-secrets / ortam değişkeninden gelir; repoda değer yoktur. Ortam (Test = Paraşüt staging, Live = canlı) uygulama
/// kimliğiyle birlikte seçilir; staging kimliği yalnız staging adresleriyle çalışır.
/// </summary>
public sealed class ParasutOptions
{
    public const string SectionName = "EDocuments:Parasut";

    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public EDocumentEnvironment Environment { get; init; } = EDocumentEnvironment.Test;

    /// <summary>OpenID Connect uçlarının tabanı (<c>…/protocol/openid-connect</c>).</summary>
    public string IdentityBaseUrl { get; init; } = "https://id.parasut.com/realms/parasut/protocol/openid-connect";

    /// <summary>API tabanı (<c>…/v4</c>).</summary>
    public string ApiBaseUrl { get; init; } = "https://api.parasut.com/v4";

    /// <summary>Paraşüt başvurusuna kayıtlı yönlendirme adresi (sürümsüz: <c>/api/e-documents/parasut/callback</c>).</summary>
    public string? RedirectUri { get; init; }

    public int TimeoutSeconds { get; init; } = 30;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(RedirectUri);
}
