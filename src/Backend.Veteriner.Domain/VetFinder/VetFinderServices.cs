namespace Backend.Veteriner.Domain.VetFinder;

/// <summary>Veteriner bul hizmeti: <see cref="Key"/> adres parçasıdır (<c>/veteriner/{il}/{ilçe}/{hizmet}</c>), değişmez.</summary>
public sealed record VetFinderService(string Key, string Name);

/// <summary>
/// Veteriner bul hizmet listesi — tek yer (TIER-015). Klinik kendi verdiği hizmetleri bu listeden seçer; sitedeki serbest hizmet
/// metinleri filtrelenebilir olmadığı için kullanılmaz. Adlar nötr bilgidir, üstünlük ya da vaat içermez (ADR-047 K6).
/// Anahtar yayından sonra değişmez: adres ve klinik seçimi ona bağlıdır.
/// </summary>
public static class VetFinderServices
{
    public static readonly IReadOnlyList<VetFinderService> All =
    [
        new("genel-muayene", "Genel muayene"),
        new("asi", "Aşı"),
        new("kisirlastirma", "Kısırlaştırma"),
        new("cerrahi", "Cerrahi"),
        new("dis-tedavisi", "Diş tedavisi"),
        new("laboratuvar", "Laboratuvar"),
        new("rontgen", "Röntgen"),
        new("ultrason", "Ultrason"),
        new("mikrocip", "Mikroçip"),
        new("acil", "Acil"),
        new("evde-muayene", "Evde muayene"),
        new("egzotik-hayvan", "Egzotik hayvan"),
    ];

    private static readonly Dictionary<string, VetFinderService> ByKey = All.ToDictionary(s => s.Key, StringComparer.Ordinal);

    public static VetFinderService? Find(string? key)
        => key is not null && ByKey.TryGetValue(key, out var service) ? service : null;
}
