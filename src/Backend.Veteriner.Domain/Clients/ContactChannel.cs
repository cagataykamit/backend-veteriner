namespace Backend.Veteriner.Domain.Clients;

/// <summary>
/// Müşterinin tercih ettiği iletişim kanalı (yalnız tercih bilgisi; rıza/onay kaydı değildir).
/// <see cref="None"/>: tercih belirtilmemiş (mevcut kayıtların varsayılanı).
/// </summary>
public enum ContactChannel
{
    None = 0,
    Phone = 1,
    Email = 2,
    Sms = 3,
    WhatsApp = 4,
}
