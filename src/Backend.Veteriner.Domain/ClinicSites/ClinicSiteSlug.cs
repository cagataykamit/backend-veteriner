using System.Text.RegularExpressions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClinicSites;

/// <summary>Adres biçim denetiminin sonucu.</summary>
public enum ClinicSiteSlugCheck
{
    Valid = 0,
    Invalid = 1,
    Reserved = 2,
}

/// <summary>
/// Sitenin <c>vetinity.com/k/{adres}</c> parçası (ADR-045 K10): küçük harf, rakam ve tire; ayrılmış sözcükler alınamaz.
/// Öneri klinik adından <see cref="TurkishSlug"/> ile üretilir. Kural tek yerde yaşar.
/// </summary>
public static partial class ClinicSiteSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 60;

    private const string FallbackBase = "klinik";

    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "admin", "yonetim", "api", "app", "www", "panel", "portal", "k", "site", "sites",
        "giris", "login", "cikis", "logout", "kayit", "register", "signup", "sifre", "hesap", "account",
        "randevu", "booking", "paketler", "fiyat", "fiyatlar", "pricing", "odeme", "abonelik",
        "destek", "yardim", "help", "support", "iletisim", "contact", "hakkimizda", "about", "blog", "sss",
        "kvkk", "gizlilik", "privacy", "sartlar", "kullanim-sartlari", "terms", "yasal", "legal",
        "vetinity", "veteriner", "veterinerler", "klinik", "klinikler",
        "static", "assets", "images", "img", "cdn", "media", "public", "favicon", "robots", "sitemap",
        "mail", "email", "smtp", "ftp", "staging", "status", "health", "test", "demo", "ornek", "null", "undefined",
    };

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>Girdiyi karşılaştırma biçimine getirir (kırpma + küçük harf); adres biçimi denetlenmez.</summary>
    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsWellFormed(string? slug)
        => slug is { Length: >= MinLength and <= MaxLength } && Pattern().IsMatch(slug);

    public static ClinicSiteSlugCheck Check(string? slug)
    {
        if (!IsWellFormed(slug))
            return ClinicSiteSlugCheck.Invalid;
        return ReservedWords.Contains(slug!) ? ClinicSiteSlugCheck.Reserved : ClinicSiteSlugCheck.Valid;
    }

    /// <summary>Klinik adından geçerli bir aday; benzersizlik denetlenmez (bkz. uygulama katmanındaki adres defteri).</summary>
    public static string Suggest(string? clinicName)
    {
        var slug = Trim(TurkishSlug.From(clinicName));
        if (slug.Length < MinLength)
            slug = slug.Length == 0 ? $"{FallbackBase}-sitesi" : $"{FallbackBase}-{slug}";
        return ReservedWords.Contains(slug) ? $"{slug}-sitesi" : slug;
    }

    /// <summary><c>{taban}-{sıra}</c> adayı; taban gerekirse kısaltılır, sonuç <see cref="MaxLength"/> içinde kalır.</summary>
    public static string WithSuffix(string slug, string suffix)
        => $"{Trim(slug, MaxLength - suffix.Length - 1)}-{suffix}";

    private static string Trim(string slug, int maxLength = MaxLength)
        => (slug.Length > maxLength ? slug[..maxLength] : slug).Trim('-');
}
