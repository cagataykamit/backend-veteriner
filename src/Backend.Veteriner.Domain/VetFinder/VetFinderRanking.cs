using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.VetFinder;

/// <summary>Enlem/boylam (derece).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>Kuş uçuşu mesafe (haversine), km.</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        static double Rad(double degrees) => degrees * Math.PI / 180.0;

        var dLat = Rad(other.Latitude - Latitude);
        var dLng = Rad(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(Rad(Latitude)) * Math.Cos(Rad(other.Latitude)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

/// <summary>Sıralamanın bir klinikten okuduğu bilgiler; il ve ilçe <see cref="TurkishSlug"/> biçimindedir.</summary>
public sealed record VetFinderRankInput(bool IsOpenNow, string CitySlug, string? DistrictSlug, GeoPoint? Location, string Name, string Key);

/// <summary>Sayfanın il/ilçesi ve (verildiyse) ziyaretçinin konumu.</summary>
public sealed record VetFinderRankReference(string? CitySlug, string? DistrictSlug, GeoPoint? Visitor);

/// <summary>
/// Veteriner bul sıralaması — tek yer (ADR-047 K3): önce "şu an açık", sonra yakınlık. Ziyaretçi konum verdiyse konumu olan
/// klinikler mesafeye göre öne gelir; konumu olmayanlar ve konum verilmediğinde aynı ilçe, sonra aynı il. Eşitlikte ad, sonra
/// anahtar: sıra kararlıdır, ödeme ya da paket sıraya girmez.
/// </summary>
public static class VetFinderRanking
{
    public static IReadOnlyList<(T Item, double? DistanceKm)> Order<T>(
        IEnumerable<T> items,
        Func<T, VetFinderRankInput> describe,
        VetFinderRankReference reference)
        => items
            .Select(item =>
            {
                var input = describe(item);
                double? distance = reference.Visitor is { } visitor && input.Location is { } location
                    ? visitor.DistanceKmTo(location)
                    : null;
                return (Item: item, Input: input, Distance: distance);
            })
            .OrderByDescending(x => x.Input.IsOpenNow)
            .ThenByDescending(x => x.Distance.HasValue)
            .ThenBy(x => x.Distance ?? 0)
            .ThenByDescending(x => reference.DistrictSlug is not null
                                   && x.Input.CitySlug == reference.CitySlug
                                   && x.Input.DistrictSlug == reference.DistrictSlug)
            .ThenByDescending(x => reference.CitySlug is not null && x.Input.CitySlug == reference.CitySlug)
            .ThenBy(x => TurkishTextKey.Normalize(x.Input.Name), StringComparer.Ordinal)
            .ThenBy(x => x.Input.Key, StringComparer.Ordinal)
            .Select(x => (x.Item, x.Distance))
            .ToList();
}

/// <summary>Veteriner bul sayfalarının sabitleri — tek yer.</summary>
public static class VetFinderPages
{
    /// <summary>
    /// K8: bu sayıdan az klinik listelenen sayfa arama motoru dizinine alınmaz (<c>noindex</c>) ve site haritasına girmez.
    /// </summary>
    public const int MinClinicsToIndex = 3;

    public const int PageSize = 24;

    /// <summary>Sonuç yoksa önerilen ilçe sayısı.</summary>
    public const int MaxSuggestedDistricts = 6;

    /// <summary>Kart başına en yakın saat isteğinde en çok anahtar (bir sayfadaki kart sayısı).</summary>
    public const int MaxNextSlotKeys = PageSize;
}
