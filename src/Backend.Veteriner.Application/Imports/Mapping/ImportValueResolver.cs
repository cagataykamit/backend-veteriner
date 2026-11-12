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
    IReadOnlyList<ImportCatalogEntry> Vaccines,
    IReadOnlySet<Guid>? PatternIds = null)
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
            ["gelincik"] = "ferretgelincik",
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
            ["almancobankopegi"] = "almancobankopegialmankurdu",
            ["germanshepherd"] = "almancobankopegialmankurdu",
            ["malteseterrier"] = "maltese",
            ["maltesebichon"] = "maltese",
            ["fransizbulldog"] = "frenchbulldog",
            ["ingilizbulldog"] = "englishbulldog",
            ["bulldog"] = "englishbulldog",
            ["pitbull"] = "pitbulltipi",
            ["labrador"] = "labradorretriever",
            ["golden"] = "goldenretriever",
            ["sibiryakurduhusky"] = "sibiryakurdusiberianhusky",
            ["husky"] = "sibiryakurdusiberianhusky",
            ["siberianhusky"] = "sibiryakurdusiberianhusky",
            ["sibiryakurdu"] = "sibiryakurdusiberianhusky",
            ["doberman"] = "dobermann",
            ["dobermanpinscher"] = "dobermann",
            ["drahthaar"] = "almanteltuylupointerdrahthaar",
            ["almanteltuylupointer"] = "almanteltuylupointerdrahthaar",
            ["cocker"] = "ingilizcockerspaniel",
            ["ingilizcocker"] = "ingilizcockerspaniel",
            ["egzoticshorthair"] = "exoticshorthair",
            ["norwegianforest"] = "norwegianforestcat",
            ["norvecormankedisi"] = "norwegianforestcat",
            ["setter"] = "ingilizsetter",
            ["kurzhaar"] = "almankisatuylupointer",
            // Macar tazısı (Magyar agár) ayrı ırktır; yalnız Vizsla adı geçen eski yazım pointer ırkına gider.
            ["vizsla"] = "vizslamacarkisatuylupointer",
            ["macartazisivizsla"] = "vizslamacarkisatuylupointer",
            ["macarvizsla"] = "vizslamacarkisatuylupointer",
            ["belcikakurdu"] = "belcikacobankopegimalinois",
            ["belgianmalinois"] = "belcikacobankopegimalinois",
            ["malinois"] = "belcikacobankopegimalinois",
            ["pekinez"] = "pekingese",
            ["americanstaffordshireterrier"] = "amerikanstaffordshireterrier",
            ["zagarkopegi"] = "zagar",
            ["abyssinian"] = "habesabyssinian",
            ["habes"] = "habesabyssinian",
            ["oriental"] = "orientalshorthair",
            ["britishbluepoint"] = "britishshorthair",
            ["britishshorthairgoldenny11"] = "britishshorthair",
            ["britishshorthairtortie"] = "britishshorthair",
            ["britishangora"] = "britishlonghair",
            ["domesticshorthair"] = "evkedisikisatuylu",
            ["domesticlonghair"] = "evkedisiuzuntuylu",
            ["amerikancocker"] = "amerikancockerspaniel",
            ["americancocker"] = "amerikancockerspaniel",
            ["rusterrier"] = "russiyahterrier",
            ["blackrussianterrier"] = "russiyahterrier",
            ["bretonyaepanyolu"] = "epagneulbretonbrittany",
            ["epanyolbreton"] = "epagneulbretonbrittany",
            ["epagneulbreton"] = "epagneulbretonbrittany",
            ["brittany"] = "epagneulbretonbrittany",
            ["avustralyacobani"] = "avustralyacobankopegi",
            ["ingilizcobankopegibobtail"] = "eskiingilizcobankopegibobtail",
            ["bobtail"] = "eskiingilizcobankopegibobtail",
            ["papagan"] = "papagandiger",
            ["safkaningilizati"] = "safkaningilizthoroughbred",
            ["ingilizati"] = "safkaningilizthoroughbred",
            ["thoroughbred"] = "safkaningilizthoroughbred",
            ["lohmanbrown"] = "lohmannbrown",
            ["turkishangora"] = "ankarakedisiturkishangora",
            ["ankara"] = "ankarakedisiturkishangora",
            ["turkishvan"] = "vankedisi",
            ["bilinmiyor"] = "diger",
            ["mix"] = "melez",
            ["kirma"] = "melez",
            // 2026-11-13 kaynak araştırması: eski katalog adları yeni FCI/FIFe/tescil adlarına.
            ["almankurdu"] = "almancobankopegialmankurdu",
            ["kangal"] = "kangalcobankopegikangal",
            ["cockerspaniel"] = "ingilizcockerspaniel",
            ["englishcockerspaniel"] = "ingilizcockerspaniel",
            ["pointer"] = "ingilizpointerpointer",
            ["englishpointer"] = "ingilizpointerpointer",
            ["pinscher"] = "almanpinscher",
            ["spitz"] = "almanspitz",
            ["germanspitz"] = "almanspitz",
            ["belcikamalinois"] = "belcikacobankopegimalinois",
            ["mastiff"] = "ingilizmastifimastiff",
            ["polonyaavkopegi"] = "polonyaavkopegigończypolski",
            ["ankarakedisi"] = "ankarakedisiturkishangora",
            ["angoratavsani"] = "ankaratavsaniangora",
            ["arapati"] = "safkanarap",
            ["pony"] = "midillipony",
            ["midilli"] = "midillipony",
            ["holstein"] = "holstaynholstein",
            ["holstayn"] = "holstaynholstein",
            ["montofon"] = "esmermontofon",
            ["esmer"] = "esmermontofon",
            ["brownswiss"] = "esmermontofon",
            ["merinos"] = "merinosdiger",
            ["ivesi"] = "ivesianadoluivesisi",
            ["beta"] = "betabetta",
            ["betta"] = "betabetta",
            ["ciklet"] = "ciklit",
            // Kürk adı ırk değildir (TDK, FIFe): ev kedisi ırkına gider; renk/desen hekimce girilir.
            ["tekir"] = "evkedisikisatuylu",
            ["sarman"] = "evkedisikisatuylu",
            ["smokin"] = "evkedisikisatuylu",
        },
        // Renk yazım farkları (sıra ve kısaltma); hedef katalog rengidir.
        [ImportValueKind.Color] = new()
        {
            ["ucrenkli"] = "ucrenklicalico",
            // 2026-11-13: renk ile desen ayrıldı; eski "Kırçıllı" yazımları kaplan çizgili desenine, birleşik satırlar desene gider.
            ["kircilli"] = "kaplancizgilibrindle",
            ["kircil"] = "kircilroan",
            ["mavi"] = "maviseyreltiksiyah",
            ["tekir"] = "tekirtabby",
            ["kaplumbaga"] = "kaplumbagakabugutortoiseshell",
            ["siyahbakir"] = "siyahbakirblackandtan",
            ["samur"] = "samursable",
            ["dumanli"] = "dumanlismoke",
            ["gumus"] = "gumussilver",
            ["lila"] = "lilalilac",
            ["tarcin"] = "tarcincinnamon",
            ["kahve"] = "kahverengi",
            ["kahvebeyaz"] = "kahverengibeyaz",
            ["beyazkahve"] = "kahverengibeyaz",
            ["kahvesiyah"] = "kahverengisiyah",
            ["siyahkahve"] = "kahverengisiyah",
            ["beyazsari"] = "saribeyaz",
            ["beyazsiyah"] = "siyahbeyaz",
            ["beyazgri"] = "gribeyaz",
            ["beyazkircilli"] = "kaplancizgilibrindle",
            ["kircillibeyaz"] = "kaplancizgilibrindle",
            ["acikkahve"] = "acikkahverengi",
            ["karisik"] = "cokrenkli",
            ["mix"] = "cokrenkli",
            ["sarisiyah"] = "siyahsari",
            ["siyahgri"] = "grisiyah",
            ["kahvekrem"] = "kahverengikrem",
            ["kremkahve"] = "kahverengikrem",
            ["beyazbej"] = "bejbeyaz",
            ["kahvekircilli"] = "kaplancizgilibrindle",
            ["kahverengikircilli"] = "kaplancizgilibrindle",
            ["acikkahvekircilli"] = "kaplancizgilibrindle",
            ["beyazsampanya"] = "sampanya",
            ["portakalbeyaz"] = "turuncubeyaz",
            ["beyazturuncu"] = "turuncubeyaz",
            ["saribeyazsiyah"] = "ucrenklicalico",
            ["alaca"] = "alacali",
            ["beyazsarikircil"] = "cokrenkli",
            ["sarman"] = "turuncu",
        },
        [ImportValueKind.Vaccine] = new()
        {
            ["rabies"] = "kuduz",
            // 2026-11-13: karma türe göre ayrıldı (köpek DHPPi±L, kedi FVRCP); "|" ile ayrılan hedeflerden hastanın türündeki seçilir.
            ["karma"] = "kopekkarmadhppil|kedikarmafvrcp",
            ["karmakuduz"] = "kopekkarmakuduz|kedikarmakuduz",
            ["bronsinbordetella"] = "bordetellaparainfluenzabronsin",
            ["losemifelv"] = "kedilosemisifelv",
            ["koronavirus"] = "kopekkoronavirusenteriticcov",
            ["dpdistemperparvo"] = "kopekdpdistemperparvovirus",
            ["parvo"] = "kopekparvoviruscpv2",
            ["newcastle"] = "newcastlehastaligi",
            ["sarbon"] = "sarbonantraks",
            ["brusella"] = "sigirbrusellozus19|koyunkecibrusellozurev1",
            ["bruselloz"] = "sigirbrusellozus19|koyunkecibrusellozurev1",
            ["klamidya"] = "kediklamidyozuchlamydiafelis",
            ["mixed"] = "kopekkarmadhppil|kedikarmafvrcp",
            ["bronsin"] = "bordetellaparainfluenzabronsin",
            ["bordetella"] = "bordetellaparainfluenzabronsin",
            ["kennelcough"] = "bordetellaparainfluenzabronsin",
            ["kopekoksurugu"] = "bordetellaparainfluenzabronsin",
            ["losemi"] = "kedilosemisifelv",
            ["felv"] = "kedilosemisifelv",
            ["corona"] = "kopekkoronavirusenteriticcov",
            ["borelyoz"] = "lymeborelyoz",
            ["icdis"] = "icdisparazit",
            ["icvedisparazit"] = "icdisparazit",
            ["mera"] = "enterotoksemi",
            ["puppydp"] = "kopekdpdistemperparvovirus",
            ["dp"] = "kopekdpdistemperparvovirus",
            ["leptospira"] = "leptospiroz",
            ["lepto"] = "leptospiroz",
            ["mantar"] = "mantardermatofitoz",
            ["dermatofit"] = "mantardermatofitoz",
            ["dermatofitoz"] = "mantardermatofitoz",
            ["atgribi"] = "atinfluenzasi",
            ["koyunkecicicegi"] = "koyunvekecicicegi",
            ["nodulerekzantem"] = "sigirlarinnodulerekzantemilsd",
            ["lsd"] = "sigirlarinnodulerekzantemilsd",
            ["kucukruminantvebasi"] = "koyunvekecivebasippr",
            ["kucukruminantlarinvebasi"] = "koyunvekecivebasippr",
            ["kucukruminantlarinvebasippr"] = "koyunvekecivebasippr",
            ["koyunkecivebasi"] = "koyunvekecivebasippr",
            ["koyunvekecivebasi"] = "koyunvekecivebasippr",
            ["koyunvebasi"] = "koyunvekecivebasippr",
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
    private readonly IReadOnlySet<Guid> _patternIds;

    public ImportValueResolver(ImportCatalogs catalogs, IEnumerable<ImportValueMapping> userMappings)
    {
        _patternIds = catalogs.PatternIds ?? new HashSet<Guid>();
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

    /// <summary>Renk kataloğundaki kayıt desen mi (hastada renk yerine desen alanına yazılır).</summary>
    public bool IsPattern(Guid colorId) => _patternIds.Contains(colorId);

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

        if (!BuiltInAliases.TryGetValue(kind, out var aliases) || !aliases.TryGetValue(key, out var aliasTarget))
            return false;

        // Türe göre ayrılmış kayıtlar ("|" ile birden çok hedef): kapsamda tek eşleşen hedef seçilir.
        foreach (var target in aliasTarget.Split('|'))
        {
            if (TryUnique(kind, target, parentId, out resolved))
                return true;
        }

        return false;
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
