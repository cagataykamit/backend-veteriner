namespace Backend.Veteriner.Domain.Clients;

/// <summary>
/// İmzalı KVKK açık rıza formunun sonucu (ADR-021). Form kâğıtta kalır; sistem yalnız sonucu ve tarihini tutar.
/// <see cref="None"/>: form alınmadı (mevcut kayıtların varsayılanı). Otomatik hatırlatma tercihini değiştirmez.
/// </summary>
public enum KvkkConsentStatus
{
    None = 0,
    Accepted = 1,
    Declined = 2,
}
