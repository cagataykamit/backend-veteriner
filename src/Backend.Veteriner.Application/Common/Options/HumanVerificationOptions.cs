namespace Backend.Veteriner.Application.Common.Options;

/// <summary>
/// İnsan doğrulamasının hangi uçlarda zorunlu olduğu (<c>Turnstile</c> bölümü; anahtarlar Infrastructure ayarındadır). Deneme kaydı
/// ve online randevu kodu her zaman korunur.
/// </summary>
public sealed class HumanVerificationOptions
{
    public const string SectionName = "Turnstile";

    /// <summary>
    /// Hasta sahibi portalında giriş kodu ve parola sıfırlama kodu isteği korunur mu. Portal mağaza uygulamasında (Capacitor WebView)
    /// widget çalışmazsa geçici kapatmak için; varsayılan açık.
    /// </summary>
    public bool ProtectPortalSignIn { get; init; } = true;
}
