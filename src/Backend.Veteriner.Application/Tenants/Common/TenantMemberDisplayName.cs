namespace Backend.Veteriner.Application.Tenants.Common;

/// <summary>
/// Tenant üye DTO'larında görünen ad alanı için geçici derivasyon.
/// <para>
/// <b>Neden geçici:</b> <see cref="Backend.Veteriner.Domain.Users.User"/> aggregate'inde bugün
/// <c>Name</c> / <c>FullName</c> / <c>DisplayName</c> türünde güvenilir bir alan yok; signup, owner-signup ve davet kabul
/// akışları yalnız e-posta + şifre topluyor. Bu yüzden tenant panelinin "üye isim" beklentisini karşılamak için
/// e-posta <c>local-part</c>'ı (<c>@</c>'ten önceki kısım) display fallback olarak kullanılır.
/// </para>
/// <para>
/// DTO alanı <c>string?</c> tutulur ve ileride domain'e gerçek <c>User.Name</c> alanı eklendiğinde kaynak buradan
/// değiştirilir; sözleşme (DTO shape) korunur (additive yol).
/// </para>
/// </summary>
public static class TenantMemberDisplayName
{
    /// <summary>
    /// E-posta için güvenli görünen ad türetir. <c>null</c>/whitespace e-posta → <c>null</c>.
    /// <c>@</c> içermeyen veya local-part'ı boş olan değerler için de <c>null</c> döner.
    /// </summary>
    public static string? DeriveFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');

        // "@..." → local-part boş; güvenli fallback null.
        if (at == 0)
            return null;

        var local = at > 0 ? trimmed[..at] : trimmed;
        local = local.Trim();
        return string.IsNullOrEmpty(local) ? null : local;
    }
}
