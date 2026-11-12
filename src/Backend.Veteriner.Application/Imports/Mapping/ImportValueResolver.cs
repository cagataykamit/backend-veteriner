using System.Text.RegularExpressions;

namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>
/// Katalog kaydı. <see cref="ParentId"/>: ırk için tür, aşı tanımı için tür (null: her tür), diğerlerinde boş.
/// </summary>
public sealed record ImportCatalogEntry(Guid Id, string Name, string? Code, Guid? ParentId);

/// <summary>Eşleme anında kiracıya görünür, <b>aktif</b> katalog kayıtları.</summary>
public sealed record ImportCatalogs(
    IReadOnlyList<ImportCatalogEntry> Species,
    IReadOnlyList<ImportCatalogEntry> Breeds,
    IReadOnlyList<ImportCatalogEntry> Colors,
    IReadOnlyList<ImportCatalogEntry> Vaccines)
{
    public IReadOnlyList<ImportCatalogEntry> Of(ImportValueKind kind) => kind switch
    {
        ImportValueKind.Species => Species,
        ImportValueKind.Breed => Breeds,
        ImportValueKind.Color => Colors,
        ImportValueKind.Vaccine => Vaccines,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

public enum ImportValueResolutionKind
{
    /// <summary>Katalog kaydına eşlendi.</summary>
    Resolved = 1,

    /// <summary>Kullanıcı bilinçli atladı (<c>targetId = null</c>).</summary>
    Skipped = 2,

    /// <summary>Eşleşme yok veya belirsiz; kullanıcı eşlemeli (yeni tanım oluşturulmaz, ADR-019 K6).</summary>
    Unmapped = 3,

    /// <summary>Kaynak değer boş.</summary>
    Empty = 4,
}

public readonly record struct ImportValueResolution(ImportValueResolutionKind Kind, Guid? TargetId)
{
    public static readonly ImportValueResolution Empty = new(ImportValueResolutionKind.Empty, null);
    public static readonly ImportValueResolution Unmapped = new(ImportValueResolutionKind.Unmapped, null);
    public static readonly ImportValueResolution Skipped = new(ImportValueResolutionKind.Skipped, null);
    public static ImportValueResolution Resolved(Guid id) => new(ImportValueResolutionKind.Resolved, id);
}

/// <summary>
/// Tür/ırk/aşı/renk adlarını katalog kaydına çözer (IMPORT_API_CONTRACT.md §5). Sıra: kullanıcı eşlemesi →
/// katalog ad/kod (katlanmış) → yerleşik takma ad → eşlenmemiş. Hiçbir zaman katalog kaydı oluşturmaz.
/// </summary>
public sealed class ImportValueResolver
{
    /// <summary>Katalog adlarının dışında kalan yaygın yazımlar (katlanmış anahtar → katlanmış katalog adı).</summary>
    private static readonly Dictionary<ImportValueKind, Dictionary<string, string>> BuiltInAliases = new()
    {
        [ImportValueKind.Species] = new()
        {
            ["dog"] = "kopek",
            ["cat"] = "kedi",
            ["bird"] = "kus",
            ["rabbit"] = "tavsan",
            ["exotic"] = "egzotik",
            ["kobay"] = "kobayginedomuzu",
            ["ginedomuzu"] = "kobayginedomuzu",
            ["papagan"] = "kus",
            ["muhabbetkusu"] = "kus",
            ["kanarya"] = "kus",
        },
        // Yazım farkları ve İngilizce adlar; hedef, hastanın türündeki katalog ırkıdır.
        [ImportValueKind.Breed] = new()
        {
            ["scottischfold"] = "scottishfold",
            ["scottishshorthair"] = "scottishstraight",
            ["pomerian"] = "pomeranian",
            ["siamessesiyamkedisi"] = "siyam",
            ["siamese"] = "siyam",
            ["irankedisi"] = "persian",
            ["iran"] = "persian",
            ["almancobankopegi"] = "almankurdu",
            ["germanshepherd"] = "almankurdu",
            ["maltese"] = "malteseterrier",
            ["fransizbulldog"] = "frenchbulldog",
            ["ingilizbulldog"] = "englishbulldog",
            ["bulldog"] = "englishbulldog",
            ["pitbull"] = "pitbulltipi",
            ["labrador"] = "labradorretriever",
            ["golden"] = "goldenretriever",
            ["sibiryakurduhusky"] = "husky",
            ["sibiryakurdu"] = "husky",
            ["cocker"] = "cockerspaniel",
            ["ingilizcocker"] = "cockerspaniel",
            ["egzoticshorthair"] = "exoticshorthair",
            ["norwegianforestcat"] = "norwegianforest",
            ["setter"] = "ingilizsetter",
            ["kurzhaar"] = "almankisatuylupointer",
            ["macartazisi"] = "macartazisivizsla",
            ["vizsla"] = "macartazisivizsla",
            ["belcikakurdu"] = "belcikamalinois",
            ["belgianmalinois"] = "belcikamalinois",
            ["malinois"] = "belcikamalinois",
            ["pekinez"] = "pekingese",
            ["americanstaffordshireterrier"] = "amerikanstaffordshireterrier",
            ["zagarkopegi"] = "zagar",
            ["abyssinian"] = "habesabyssinian",
            ["habes"] = "habesabyssinian",
            ["orientallonghair"] = "oriental",
            ["orientalshorthair"] = "oriental",
            ["britishbluepoint"] = "britishshorthair",
            ["britishshorthairgoldenny11"] = "britishshorthair",
            ["britishshorthairtortie"] = "britishshorthair",
            ["britishangora"] = "britishlonghair",
            ["domesticshorthair"] = "melez",
            ["amerikancocker"] = "amerikancockerspaniel",
            ["americancocker"] = "amerikancockerspaniel",
            ["rusterrier"] = "russiyahterrieri",
            ["russiyahterrier"] = "russiyahterrieri",
            ["blackrussianterrier"] = "russiyahterrieri",
            ["bretonyaepanyolu"] = "epanyolbreton",
            ["brittany"] = "epanyolbreton",
            ["tibetterrier"] = "tibetterrieri",
            ["avustralyaterrier"] = "avustralyaterrieri",
            ["avustralyacobani"] = "avustralyacobankopegi",
            ["ingilizcobankopegibobtail"] = "eskiingilizcobankopegibobtail",
            ["bobtail"] = "eskiingilizcobankopegibobtail",
            ["papagan"] = "papagandiger",
            ["safkaningilizati"] = "ingilizati",
            ["turkishangora"] = "ankarakedisi",
            ["ankara"] = "ankarakedisi",
            ["turkishvan"] = "vankedisi",
            ["bilinmiyor"] = "diger",
            ["mix"] = "melez",
            ["kirma"] = "melez",
        },
        // Renk yazım farkları (sıra ve kısaltma); hedef katalog rengidir.
        [ImportValueKind.Color] = new()
        {
            ["ucrenkli"] = "alacali",
            ["tricolor"] = "alacali",
            ["calico"] = "alacali",
            ["kahve"] = "kahverengi",
            ["kahvebeyaz"] = "kahverengibeyaz",
            ["beyazkahve"] = "kahverengibeyaz",
            ["kahvesiyah"] = "kahverengisiyah",
            ["siyahkahve"] = "kahverengisiyah",
            ["beyazsari"] = "saribeyaz",
            ["beyazsiyah"] = "siyahbeyaz",
            ["beyazgri"] = "gribeyaz",
            ["beyazkircilli"] = "kircillibeyaz",
            ["acikkahve"] = "acikkahverengi",
            ["karisik"] = "cokrenkli",
            ["mix"] = "cokrenkli",
            ["sarisiyah"] = "siyahsari",
            ["siyahgri"] = "grisiyah",
            ["kahvekrem"] = "kahverengikrem",
            ["kremkahve"] = "kahverengikrem",
            ["beyazbej"] = "bejbeyaz",
            ["kahvekircilli"] = "kahverengikircilli",
            ["acikkahvekircilli"] = "kahverengikircilli",
            ["beyazsampanya"] = "sampanya",
            ["portakalbeyaz"] = "turuncubeyaz",
            ["beyazturuncu"] = "turuncubeyaz",
            ["saribeyazsiyah"] = "alacali",
            ["beyazsarikircil"] = "cokrenkli",
            ["sarman"] = "turuncu",
        },
        [ImportValueKind.Vaccine] = new()
        {
            ["rabies"] = "kuduz",
            ["mixed"] = "karma",
            ["bronchine"] = "bronsin",
            ["kennelcough"] = "bronsin",
            ["leukemia"] = "losemi",
            ["felv"] = "losemi",
            ["icdis"] = "icdisparazit",
            ["icvedisparazit"] = "icdisparazit",
            ["mera"] = "enterotoksemi",
            ["puppydp"] = "dpdistemperparvo",
            ["dp"] = "dpdistemperparvo",
            ["leptospira"] = "leptospiroz",
            ["lepto"] = "leptospiroz",
            ["dermatofit"] = "mantar",
            // Ticari ürün adları (kombine iç-dış / yalnız dış / yalnız iç parazit)
            ["nexgardspectra"] = "icdisparazit",
            ["nexgardcombo"] = "icdisparazit",
            ["simparicatrio"] = "icdisparazit",
            ["broadline"] = "icdisparazit",
            ["advocate"] = "icdisparazit",
            ["stronghold"] = "icdisparazit",
            ["strongholdplus"] = "icdisparazit",
            ["revolution"] = "icdisparazit",
            ["nexgard"] = "disparazit",
            ["bravecto"] = "disparazit",
            ["seresto"] = "disparazit",
            ["scalibor"] = "disparazit",
            ["frontline"] = "disparazit",
            ["simparica"] = "disparazit",
            ["credelio"] = "disparazit",
            ["advantix"] = "disparazit",
            ["milbemax"] = "icparazit",
            ["drontal"] = "icparazit",
        },
    };

    private readonly Dictionary<ImportValueKind, ILookup<string, ImportCatalogEntry>> _byKey = new();
    private readonly Dictionary<(ImportValueKind Kind, string Key), Guid?> _userMappings = new();

    public ImportValueResolver(ImportCatalogs catalogs, IEnumerable<ImportValueMapping> userMappings)
    {
        foreach (var kind in Enum.GetValues<ImportValueKind>())
        {
            _byKey[kind] = catalogs.Of(kind)
                .SelectMany(e => new[] { ImportTextFolding.Fold(e.Name), ImportTextFolding.Fold(e.Code) }
                    .Where(k => k.Length > 0)
                    .Distinct()
                    .Select(k => (Key: k, Entry: e)))
                .ToLookup(x => x.Key, x => x.Entry);
        }

        foreach (var mapping in userMappings)
        {
            if (ImportValueKinds.TryParse(mapping.Kind, out var kind))
                _userMappings[(kind, ImportTextFolding.Fold(mapping.Source))] = mapping.TargetId;
        }
    }

    /// <param name="parentId">
    /// Irk için hayvanın türü (zorunlu: türsüz ırk eşlenmez); aşı için hastanın türü (tanımın türü doluysa aynı olmalı).
    /// </param>
    public ImportValueResolution Resolve(ImportValueKind kind, string? source, Guid? parentId = null)
    {
        var key = ImportTextFolding.Fold(source);
        if (key.Length == 0)
            return ImportValueResolution.Empty;

        if (_userMappings.TryGetValue((kind, key), out var target))
            return target is { } id ? ImportValueResolution.Resolved(id) : ImportValueResolution.Skipped;

        if (TryCatalogOrAlias(kind, key, parentId, out var resolved))
            return resolved;

        // Aşı adı doz/yıl/tür ekleriyle yazılmışsa (YILLIK KEDİ KARMA, LÖSEMİ 2, İÇ PARAZİT 2 AYLIK) aşı serisinin adıyla denenir.
        if (kind == ImportValueKind.Vaccine && ReduceVaccineKey(key) is var series && series != key
            && TryCatalogOrAlias(kind, series, parentId, out resolved))
        {
            return resolved;
        }

        return ImportValueResolution.Unmapped;
    }

    /// <summary>
    /// Aşı serisi anahtarı: katlanmış addan baştaki/sondaki <c>yıllık</c>, sondaki doz numarası ve <c>N aylık</c>, sondaki
    /// <c>aşı</c>/<c>aşısı</c>, baştaki <c>kedi</c>/<c>köpek</c> atılır (<c>YILLIK KEDİ KARMA</c> = <c>KEDİ KARMA 2</c> = <c>Karma Aşısı</c> =
    /// <c>karma</c>). Sonraki aşı tarihini serinin
    /// son uygulamasına bağlamak (E-vet dönüştürücüsü) ve katalog eşlemesi aynı kuralı kullanır.
    /// </summary>
    public static string VaccineSeriesKey(string? vaccineName) => ReduceVaccineKey(ImportTextFolding.Fold(vaccineName));

    /// <summary>
    /// Eski programın aşı paketinde aşı olmayan uygulama (tedavi, bağışıklık destek ürünü): adında <c>tedavi</c> geçer ya da bilinen
    /// tedavi ürünüdür. Aşı geçmişine değil klinik geçmişe yazılır; aşı kataloğuna eklenmez.
    /// </summary>
    public static bool IsTreatment(string? vaccineName)
    {
        var key = ImportTextFolding.Fold(vaccineName);
        return key.Contains("tedavi", StringComparison.Ordinal) || TreatmentProducts.Contains(VaccineSeriesKey(vaccineName));
    }

    /// <summary>Aşı paketine yazılan, aşı olmayan ürünler (katlanmış seri anahtarı): bağışıklık destek ve homeopatik ürünler.</summary>
    private static readonly HashSet<string> TreatmentProducts = new(StringComparer.Ordinal)
    {
        "zylexis", "theranekron", "theranekrond", "baypamun",
    };

    private static string ReduceVaccineKey(string key)
    {
        var reduced = VaccineYearly.Replace(key, string.Empty);
        reduced = VaccineDoseSuffix.Replace(reduced, string.Empty);
        reduced = VaccineWordSuffix.Replace(reduced, string.Empty);
        reduced = VaccineSpeciesPrefix.Replace(reduced, string.Empty);
        return reduced.Length == 0 ? key : reduced;
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex VaccineYearly = new("^yillik|yillik$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex VaccineDoseSuffix = new(@"\d*aylik$|\d+$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex VaccineWordSuffix = new("(?<=.{3})(asisi|asi)$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex VaccineSpeciesPrefix = new("^(kedi|kopek)(?=.)", RegexOptions.Compiled, RegexTimeout);

    private bool TryCatalogOrAlias(ImportValueKind kind, string key, Guid? parentId, out ImportValueResolution resolved)
    {
        if (TryUnique(kind, key, parentId, out resolved))
            return true;

        return BuiltInAliases.TryGetValue(kind, out var aliases)
               && aliases.TryGetValue(key, out var aliasTarget)
               && TryUnique(kind, aliasTarget, parentId, out resolved);
    }

    private bool TryUnique(ImportValueKind kind, string key, Guid? parentId, out ImportValueResolution resolution)
    {
        var candidates = _byKey[kind][key]
            .Where(e => InScope(kind, e, parentId))
            .DistinctBy(e => e.Id)
            .Take(2)
            .ToList();

        // Belirsiz (birden fazla kayıt) otomatik eşlenmez; kullanıcı seçer.
        if (candidates.Count == 1)
        {
            resolution = ImportValueResolution.Resolved(candidates[0].Id);
            return true;
        }

        resolution = ImportValueResolution.Unmapped;
        return false;
    }

    private static bool InScope(ImportValueKind kind, ImportCatalogEntry entry, Guid? parentId) => kind switch
    {
        ImportValueKind.Breed => parentId is not null && entry.ParentId == parentId,
        ImportValueKind.Vaccine => entry.ParentId is null || parentId is null || entry.ParentId == parentId,
        _ => true,
    };
}
