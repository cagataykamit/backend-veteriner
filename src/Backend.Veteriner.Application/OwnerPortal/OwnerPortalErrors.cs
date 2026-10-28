using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.OwnerPortal;

/// <summary>Numaralandırmaya karşı tek tip portal kimlik hataları (hesap var/yok, kilit, yanlış kod ayırt edilmez).</summary>
public static class OwnerPortalErrors
{
    public static readonly Error InvalidCredentials = new(
        "Portal.Auth.Unauthorized.InvalidCredentials",
        "E-posta, kod veya parola hatalı ya da çok fazla deneme yapıldı. Bir süre sonra tekrar deneyin.");

    public static readonly Error InvalidRefreshToken = new(
        "Portal.Auth.Unauthorized.InvalidRefreshToken",
        "Oturum geçersiz veya süresi dolmuş; yeniden giriş yapın.");

    /// <summary>Oturumdaki hesap pasif ya da silinmiş; portal uçları 401 döner.</summary>
    public static readonly Error AccountUnavailable = new(
        "Portal.Auth.Unauthorized.AccountUnavailable",
        "Portal hesabı kullanılamıyor; yeniden giriş yapın.");

    /// <summary>Hesap silme onay kodu yanlış, süresi dolmuş, kullanılmış ya da deneme hakkı bitmiş (ADR-041 K2).</summary>
    public static readonly Error DeletionCodeInvalid = new(
        "Portal.Account.DeletionCodeInvalid",
        "Onay kodu hatalı ya da süresi dolmuş. Yeni kod isteyin.");

    /// <summary>Hayvan bağın müşterisine ait değil (başka müşteri/kiracı ya da hiç yok; ayırt edilmez).</summary>
    public static readonly Error PetNotFound = new("Portal.Pets.NotFound", "Kayıt bulunamadı.");

    public static readonly Error RefreshTokenReused = new(
        "Portal.Auth.Unauthorized.RefreshTokenReused",
        "Oturum jetonu tekrar kullanıldı; güvenlik için tüm oturumlar kapatıldı. Yeniden giriş yapın.");
}
