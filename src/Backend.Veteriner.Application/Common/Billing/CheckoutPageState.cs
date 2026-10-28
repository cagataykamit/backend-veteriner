namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>Açık oturumun sağlayıcı ödeme sayfasının durumu; oturum yeniden kullanılmadan önce sorulur.</summary>
public enum CheckoutPageState
{
    /// <summary>Sayfa hâlâ ödenebilir; aynı oturum kullanılır.</summary>
    Payable,

    /// <summary>Sayfa kullanılamaz (ödeme denemesi sonrası tükenmiş, geçersiz ya da durum alınamadı); oturum yenilenir.</summary>
    Unusable,

    /// <summary>Ödeme alınmış, aktivasyon (webhook) bekleniyor; oturum kapatılmaz, yenisi açılmaz.</summary>
    Paid,
}
