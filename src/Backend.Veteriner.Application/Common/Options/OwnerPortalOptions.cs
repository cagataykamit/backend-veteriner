namespace Backend.Veteriner.Application.Common.Options;

/// <summary>
/// Hasta sahibi portalı ayarları (ADR-031; sözleşme <c>docs/PORTAL_API_CONTRACT.md</c> §8). Kod/kilit kuralları ayar değil,
/// alan kuralıdır (<c>Domain/OwnerPortal</c>).
/// </summary>
public sealed class OwnerPortalOptions
{
    public const string SectionName = "OwnerPortal";

    /// <summary>Portal access token hedef kitlesi; personel <c>Jwt:Audience</c> ile aynı olamaz.</summary>
    public string Audience { get; init; } = "Backend.Veteriner.Portal";

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 30;

    public int InviteExpiryDays { get; init; } = 7;

    /// <summary>Davet bağlantısı kökü; boşsa <c>{App:FrontendBaseUrl}/portal</c>.</summary>
    public string PortalBaseUrl { get; init; } = "";
}
