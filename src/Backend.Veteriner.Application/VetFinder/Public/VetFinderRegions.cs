using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.VetFinder.Public;

public sealed record VetFinderDistrictGroup(string Slug, string Name, IReadOnlyList<VetFinderEntry> Entries);

public sealed record VetFinderCityGroup(string Slug, string Name, IReadOnlyList<VetFinderEntry> Entries, IReadOnlyList<VetFinderDistrictGroup> Districts);

/// <summary>
/// İl ve ilçe gruplaması — tek yer. Klinik kaydındaki il/ilçe serbest metindir; aynı adres biçimine (<c>istanbul</c>) düşen yazımlar
/// tek yer sayılır ("İstanbul", "istanbul", "ISTANBUL"). Görünen ad en çok kullanılan yazımdır. Sıra Türkçe karşılaştırma anahtarına göre.
/// </summary>
public static class VetFinderRegions
{
    public static IReadOnlyList<VetFinderCityGroup> Group(IReadOnlyList<VetFinderEntry> entries)
        => entries
            .Where(e => e.CitySlug.Length > 0)
            .GroupBy(e => e.CitySlug, StringComparer.Ordinal)
            .Select(city => new VetFinderCityGroup(
                city.Key,
                DisplayName(city.Select(e => e.City)),
                city.ToList(),
                city.Where(e => e.DistrictSlug is not null)
                    .GroupBy(e => e.DistrictSlug!, StringComparer.Ordinal)
                    .Select(d => new VetFinderDistrictGroup(d.Key, DisplayName(d.Select(e => e.District!)), d.ToList()))
                    .OrderBy(d => TurkishTextKey.Normalize(d.Name), StringComparer.Ordinal)
                    .ToList()))
            .OrderBy(c => TurkishTextKey.Normalize(c.Name), StringComparer.Ordinal)
            .ToList();

    private static string DisplayName(IEnumerable<string> spellings)
        => spellings
            .GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .First()
            .Key;
}
