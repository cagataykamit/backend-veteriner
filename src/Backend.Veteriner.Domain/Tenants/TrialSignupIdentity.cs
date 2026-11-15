using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Deneme kaydının kimliği (TRIAL-001): bir e-posta adresiyle ve bir vergi numarasıyla (VKN ya da TCKN) yalnız bir deneme hesabı
/// açılır. Satır kiracı silinse de kalır (kiracıya ait değil, yabancı anahtar yok); benzersizlik veritabanı indeksiyle korunur.
/// E-posta anahtarı ve vergi numarası saklama biçimi yalnız burada yaşar; numara doğrulaması <see cref="TurkishTaxIdentity"/>'dedir.
/// </summary>
public sealed class TrialSignupIdentity
{
    public const int EmailKeyMaxLength = 200;

    public Guid Id { get; private set; }

    /// <summary>Denemeyi açan kiracı. Adı bilerek <c>TenantId</c> değil: satır kiracıya ait değildir, kiracı filtresine girmez (ADR-040).</summary>
    public Guid TrialTenantId { get; private set; }

    /// <summary>Tek deneme anahtarı: küçük harf, kırpılmış, yerel kısımdaki <c>+etiket</c> atılmış (<see cref="ToEmailKey"/>).</summary>
    public string EmailKey { get; private set; } = default!;

    /// <summary>VKN (10 hane) ya da TCKN (11 hane), <see cref="TurkishTaxIdentity.Normalize"/> biçiminde; zorunlu değilken boş olabilir.</summary>
    public string? TaxIdentityNumber { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private TrialSignupIdentity() { }

    public static TrialSignupIdentity Create(Guid tenantId, string email, string? taxIdentityNumber, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Kiracı kimliği geçersiz.", nameof(tenantId));

        var key = ToEmailKey(email) ?? throw new ArgumentException("E-posta boş olamaz.", nameof(email));
        var tax = TurkishTaxIdentity.Normalize(taxIdentityNumber);
        if (tax is not null && !TurkishTaxIdentity.IsValidTaxpayerIdentity(tax))
            throw new ArgumentException("Vergi numarası geçersiz.", nameof(taxIdentityNumber));

        return new TrialSignupIdentity
        {
            Id = Guid.NewGuid(),
            TrialTenantId = tenantId,
            EmailKey = key,
            TaxIdentityNumber = tax,
            CreatedAtUtc = utcNow,
        };
    }

    /// <summary>
    /// Aynı kutuya giden adres çeşitlemelerini tek anahtara indirir: <c>Ad.Soyad+deneme2@Ornek.com</c> → <c>ad.soyad@ornek.com</c>.
    /// Noktalar korunur (her sağlayıcıda aynı kutu değildir).
    /// </summary>
    public static string? ToEmailKey(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalized = email.Trim().ToLowerInvariant();
        var at = normalized.LastIndexOf('@');
        if (at <= 0)
            return normalized;

        var local = normalized[..at];
        var plus = local.IndexOf('+');
        return plus > 0 ? string.Concat(local.AsSpan(0, plus), normalized.AsSpan(at)) : normalized;
    }
}
