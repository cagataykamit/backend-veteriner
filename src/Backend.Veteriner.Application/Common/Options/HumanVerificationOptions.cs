namespace Backend.Veteriner.Application.Common.Options;

/// <summary>
/// İnsan doğrulamasının hangi uçlarda zorunlu olduğu (<c>Turnstile</c> bölümü; anahtarlar Infrastructure ayarındadır). Deneme kaydı
/// e-posta kodu her zaman korunur.
/// </summary>
public sealed class HumanVerificationOptions
{
    public const string SectionName = "Turnstile";

    /// <summary>
    /// Hasta sahibi portalında giriş kodu, parola sıfırlama kodu ve online randevu e-posta kodu isteği korunur mu; varsayılan açık.
    /// Portal mağaza uygulamasında (Capacitor WebView) widget çalışmazsa kapatmak için. İstek başlığına göre istisna yoktur (sahte
    /// başlıkla aşılır); kapalıyken de hız sınırları çalışır.
    /// </summary>
    public bool EnforcePortal { get; init; } = true;
}
