namespace Backend.Veteriner.Application.Clients;

/// <summary>
/// Müşteri telefonu kuralı (kullanıcı dostu denetim H17, 2026-11-15): yeni müşteride (hızlı kayıt ve tam form) telefon
/// zorunludur; içe aktarma muaftır (eski veride telefon olmayabilir). Düzenlemede kayıtlı telefon silinemez; telefonu hiç
/// olmayan (eski ya da içe aktarılmış) kayıt telefonsuz düzenlenebilir. Biçim <see cref="Domain.Clients.TurkishMobilePhone"/>.
/// </summary>
public static class ClientPhoneRule
{
    public const string RequiredCode = "Clients.PhoneRequired";
    public const string RequiredMessage = "Telefon numarası gereklidir. Örnek: 0532 111 22 33";
    public const string InvalidMessage = "Telefon numarası geçerli değil. Türkiye cep telefonu girin, örnek: 0532 111 22 33";

    /// <summary>Yeni müşteri: içe aktarma dışında telefon dolu olmalı.</summary>
    public static bool IsSatisfiedOnCreate(string? phone, bool fromImport)
        => fromImport || !string.IsNullOrWhiteSpace(phone);

    /// <summary>Düzenleme: kayıtlı telefon varken boş gönderilemez.</summary>
    public static bool IsSatisfiedOnUpdate(string? phone, string? storedPhone)
        => storedPhone is null || !string.IsNullOrWhiteSpace(phone);
}
