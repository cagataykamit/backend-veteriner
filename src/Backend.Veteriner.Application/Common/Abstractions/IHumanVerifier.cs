namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Görünmez insan doğrulaması (Cloudflare Turnstile): herkese açık, e-posta gönderen ya da hesap açan uçları botlara karşı korur.
/// Sağlayıcı ve anahtarlar yapılandırmadadır (<c>Turnstile:*</c>). Yapılandırılmamışsa korunan uç açık bir hatayla durur;
/// doğrulamasız devam edilmez.
/// </summary>
public interface IHumanVerifier
{
    /// <summary>Doğrulama yapılabilir mi (üretimde anahtarlar girilmemişse <c>false</c>).</summary>
    bool IsConfigured { get; }

    /// <summary>İstemcinin widget'ı çizmek için kullandığı herkese açık site anahtarı; doğrulama gerekmiyorsa (yerel sahte) boş.</summary>
    string? SiteKey { get; }

    /// <param name="token">İstemcideki widget'ın ürettiği tek kullanımlık jeton.</param>
    /// <param name="remoteIp">İsteğin IP adresi (sağlayıcıya ek sinyal; bilinmiyorsa boş).</param>
    /// <param name="action">Widget'ın <c>action</c> değeri; yanıttaki değerle karşılaştırılır (<see cref="HumanVerificationActions"/>).</param>
    Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken ct);
}

public enum HumanVerificationResult
{
    Passed,

    /// <summary>Jeton yok, geçersiz, süresi dolmuş, kullanılmış ya da başka bir eylem/site için.</summary>
    Failed,

    /// <summary>Doğrulama sağlayıcısına ulaşılamadı ya da yapılandırılmamış.</summary>
    Unavailable,
}

/// <summary>Widget <c>action</c> değerleri (en çok 32 karakter; harf, rakam, <c>_</c>, <c>-</c>). İstemciyle aynı olmalı.</summary>
public static class HumanVerificationActions
{
    public const string SignupEmailCode = "signup-email-code";
    public const string PortalSignInCode = "portal-signin-code";
    public const string PortalPasswordReset = "portal-password-reset";
    public const string OnlineBookingCode = "booking-email-code";
}
