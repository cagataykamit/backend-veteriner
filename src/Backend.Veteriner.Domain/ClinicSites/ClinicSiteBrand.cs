using System.Text.Json;
using System.Text.RegularExpressions;

namespace Backend.Veteriner.Domain.ClinicSites;

/// <summary>
/// Sitenin marka alanları (APPT-022): online randevu sayfası kliniğin logosunu ve ana rengini yayındaki siteden alır; ikinci bir
/// yükleme alanı yoktur. "Backend tasarım alanlarını yorumlamaz" kuralının (<see cref="ClinicSiteConfig"/>) tek istisnasıdır ve
/// yalnız iki yolu okur: <c>content.clinic.logo</c> (yalnız <c>site-image:{kimlik}</c>; hazır fotoğraf logo sayılmaz) ve
/// <c>design.colors.primary</c> (yalnız <c>#rrggbb</c>). Alan yoksa ya da biçimi uymazsa boş döner. Rengin okunabilirliği
/// sayfanın işidir: kontrastı yetmeyen renkte sayfa kendi varsayılanına döner.
/// </summary>
public sealed partial record ClinicSiteBrand(Guid? LogoImageId, string? AccentColor)
{
    public static readonly ClinicSiteBrand None = new(null, null);

    public static ClinicSiteBrand Read(string configJson)
    {
        try
        {
            using var document = JsonDocument.Parse(configJson);
            var root = document.RootElement;
            return new ClinicSiteBrand(LogoOf(root), ColorOf(root));
        }
        catch (JsonException)
        {
            return None;
        }
    }

    private static Guid? LogoOf(JsonElement root)
    {
        var value = Text(root, "content", "clinic", "logo");
        return value is not null
               && value.StartsWith(ClinicSiteConfig.ImageReferencePrefix, StringComparison.Ordinal)
               && Guid.TryParse(value[ClinicSiteConfig.ImageReferencePrefix.Length..], out var id)
            ? id
            : null;
    }

    private static string? ColorOf(JsonElement root)
    {
        var value = Text(root, "design", "colors", "primary")?.Trim();
        return value is not null && HexColor().IsMatch(value) ? value.ToLowerInvariant() : null;
    }

    private static string? Text(JsonElement element, params string[] path)
    {
        foreach (var key in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
