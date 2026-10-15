using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>
/// Kanonik alanın tanımı. <see cref="Aliases"/> yalnız kanonik adın dışındaki yaygın başlık yazımlarıdır (katlanmış
/// karşılaştırılır). <see cref="Description"/>/<see cref="Example"/>/<see cref="IsText"/> şablonun açıklama sayfasını ve
/// sütun biçimini besler (kural metni IMPORT_API_CONTRACT.md §4.3 ile aynıdır).
/// <see cref="Name"/> API anahtarıdır (eşleme isteği/yanıtı, sorun <c>field</c>) ve değişmez; <see cref="Header"/> şablonda
/// kullanıcıya görünen başlıktır (ADR-022: hayvan kaydına "hasta" denir). İkisi de kesin (Exact) eşleşir; böylece eski
/// şablonun başlığı da tanınır.
/// </summary>
public sealed record ImportCanonicalField(
    string Name,
    string RequiredNote,
    string Description,
    string Example,
    bool IsText,
    params string[] Aliases)
{
    public string Header { get; init; } = Name;
}

/// <summary>Bir tablonun zorunlu alan kuralı: <see cref="AllOf"/> hepsi, <see cref="AnyOf"/> gruplardan en az biri tam dolu.</summary>
public sealed record ImportRequirement(string[] AllOf, string[][] AnyOf);

public enum ImportSuggestionConfidence
{
    None = 0,
    Exact = 1,
    Alias = 2,
}

public sealed record ImportColumnSuggestion(string Source, string? Suggested, ImportSuggestionConfidence Confidence);

/// <summary>Kanonik başlıklar, takma adlar ve zorunlu alan kuralları — tek yer (IMPORT_API_CONTRACT.md §4.2-4.3).</summary>
public static class ImportCanonicalSchema
{
    private const string PetNoHeader = "HastaNo";
    private const string PetNameHeader = "HastaAdi";
    private const string VaccinationPetLinkNote = "Evet (biri): HastaNo veya SahipAdSoyad + SahipTelefon + HastaAdi + Tur";

    private static readonly ImportCanonicalField[] CustomerFields =
    [
        new("MusteriNo", "Hayır (hasta bu anahtarla bağlanacaksa evet)", "Eski sistemdeki müşteri referansı; dosya içinde benzersiz olmalı.", "M-1001", true,
            "Müşteri No", "Müşteri Numarası", "Müşteri Kodu", "Cari Kodu", "Cari No", "Sahip No"),
        new("AdSoyad", "Evet", "2-300 karakter.", "Ayşe Yılmaz", false,
            "Ad Soyad", "Adı Soyadı", "Ad Soyadı", "İsim", "İsim Soyisim", "Müşteri", "Müşteri Adı", "Sahip", "Sahip Adı", "Sahibi", "Ünvan"),
        new("Telefon", "Hayır (önerilir)", "Türkiye cep telefonu: 05XXXXXXXXX veya 5XXXXXXXXX veya +90 5XX XXX XX XX.", "05321234567", true,
            "Tel", "Cep", "Cep Telefonu", "Cep Tel", "GSM", "Telefon No", "Telefon Numarası", "Mobil", "Phone"),
        new("Eposta", "Hayır", "Geçerli e-posta adresi (en çok 320 karakter).", "ayse@example.com", false,
            "E-posta", "E posta", "Email", "E-mail", "Mail", "Eposta Adresi"),
        new("Adres", "Hayır", "En çok 500 karakter.", "Örnek Mah. Örnek Sok. No:1", false,
            "Adresi", "Açık Adres", "Address"),
    ];

    private static readonly ImportCanonicalField[] PetFields =
    [
        new("MusteriNo", "Evet (biri): MusteriNo veya SahipAdSoyad + SahipTelefon", "Sahibe bağlantı; müşteri sayfasındaki MusteriNo ile aynı değer.", "M-1001", true,
            "Müşteri No", "Müşteri Numarası", "Müşteri Kodu", "Cari Kodu", "Sahip No"),
        new("SahipAdSoyad", "Evet (biri): MusteriNo veya SahipAdSoyad + SahipTelefon", "Sahibin adı soyadı.", "Ayşe Yılmaz", false,
            "Sahip Ad Soyad", "Sahip", "Sahip Adı", "Sahibi", "Müşteri", "Müşteri Adı", "Müşteri Ad Soyad"),
        new("SahipTelefon", "Evet (biri): MusteriNo veya SahipAdSoyad + SahipTelefon", "Sahibin cep telefonu.", "05321234567", true,
            "Sahip Tel", "Sahip Cep", "Sahip Telefonu", "Müşteri Telefon", "Müşteri Tel", "Telefon", "Cep"),
        new("HayvanNo", "Hayır (aşı bu anahtarla bağlanacaksa evet)", "Eski sistemdeki hasta referansı.", "H-2001", true,
            "Hayvan No", "Hasta No", "Hayvan Numarası", "Hasta Numarası", "Hasta Kodu", "Hayvan Kodu") { Header = PetNoHeader },
        new("HayvanAdi", "Evet", "1-200 karakter.", "Pamuk", false,
            "Hayvan Adı", "Hasta Adı", "Hayvan", "Hasta", "Adı", "Ad", "İsim", "Pet Adı", "Evcil Hayvan") { Header = PetNameHeader },
        new("Tur", "Evet", "Katalogdaki hayvan türü (Köpek, Kedi, ...).", "Kedi", false,
            "Tür", "Hayvan Türü", "Hayvan Tür"),
        new("Irk", "Hayır", "Türe ait ırk; katalogda yoksa serbest metin olarak saklanır.", "Tekir", false,
            "Cins", "Irkı"),
        new("Cinsiyet", "Hayır", "Erkek / Dişi.", "Dişi", false,
            "Cinsiyeti", "Sex", "Gender"),
        new("DogumTarihi", "Hayır", "gg.aa.yyyy veya yyyy-aa-gg; gelecekte olamaz.", "15.03.2021", true,
            "Doğum Tarihi", "Doğum Tar", "D. Tarihi", "Doğum"),
        new("Kisir", "Hayır", "Evet / Hayır.", "Evet", false,
            "Kısır", "Kısırlaştırılmış", "Kısırlaştırma", "Kısırlık"),
        new("Mikrocip", "Hayır", "Yalnız rakam (genelde 15 hane); hücreyi METİN biçiminde tutun.", "985112345678901", true,
            "Mikroçip", "Mikroçip No", "Mikrocip No", "Çip", "Çip No", "Chip", "Microchip"),
        new("Renk", "Hayır", "Katalogda yoksa nota eklenir.", "Sarı", false,
            "Tüy Rengi", "Renkler"),
        new("Agirlik", "Hayır", "Kilogram; ondalık virgül veya nokta.", "4,5", false,
            "Ağırlık", "Kilo", "Kg", "Ağırlık (kg)"),
        new("KarneNo", "Hayır", "Pasaport / karne numarası (en çok 50 karakter).", "K-77", true,
            "Karne No", "Karne", "Pasaport No", "Pasaport"),
        new("Not", "Hayır", "Serbest not (en çok 2000 karakter).", "Sakin", false,
            "Notlar", "Açıklama", "Özel Not"),
    ];

    private static readonly ImportCanonicalField[] VaccinationFields =
    [
        new("HayvanNo", VaccinationPetLinkNote, "Hasta sayfasındaki HastaNo ile aynı değer.", "H-2001", true,
            "Hayvan No", "Hasta No", "Hayvan Numarası", "Hasta Numarası", "Hasta Kodu", "Hayvan Kodu") { Header = PetNoHeader },
        new("SahipAdSoyad", VaccinationPetLinkNote, "Sahibin adı soyadı.", "Ayşe Yılmaz", false,
            "Sahip Ad Soyad", "Sahip", "Sahip Adı", "Sahibi", "Müşteri", "Müşteri Adı"),
        new("SahipTelefon", VaccinationPetLinkNote, "Sahibin cep telefonu.", "05321234567", true,
            "Sahip Tel", "Sahip Cep", "Müşteri Telefon", "Telefon", "Cep"),
        new("HayvanAdi", VaccinationPetLinkNote, "Hastanın adı.", "Pamuk", false,
            "Hayvan Adı", "Hasta Adı", "Hayvan", "Hasta", "Pet Adı") { Header = PetNameHeader },
        new("Tur", VaccinationPetLinkNote, "Hayvan türü.", "Kedi", false,
            "Tür", "Hayvan Türü"),
        new("AsiAdi", "Evet", "Katalogdaki aşı tanımı (Kuduz, Karma, ...).", "Kuduz", false,
            "Aşı Adı", "Aşı", "Aşı Türü", "Uygulanan Aşı", "Vaccine"),
        new("UygulamaTarihi", "Evet", "gg.aa.yyyy veya yyyy-aa-gg; gelecekte olamaz, 1990'dan eski olamaz.", "10.04.2024", true,
            "Uygulama Tarihi", "Aşı Tarihi", "Tarih", "Uygulanma Tarihi", "Yapılış Tarihi"),
        new("SonrakiAsiTarihi", "Hayır", "Uygulama tarihinden sonra olmalı.", "10.04.2025", true,
            "Sonraki Aşı Tarihi", "Sonraki Aşı", "Bir Sonraki Aşı", "Tekrar Tarihi", "Rapel Tarihi", "Sonraki Tarih", "Vade"),
        new("Seri", "Hayır", "Seri / lot numarası (Not alanına eklenir).", "L-5521", true,
            "Seri No", "Lot", "Lot No"),
        new("Uygulayan", "Hayır", "Aşıyı yapan (Not alanına eklenir).", "Dr. Örnek", false,
            "Uygulayan Hekim", "Hekim", "Veteriner"),
        new("Not", "Hayır", "Serbest not.", "Doz 1", false,
            "Notlar", "Açıklama"),
    ];

    private static readonly Dictionary<ImportTable, ImportRequirement> Requirements = new()
    {
        [ImportTable.Customers] = new(["AdSoyad"], []),
        [ImportTable.Pets] = new(
            ["HayvanAdi", "Tur"],
            [["MusteriNo"], ["SahipAdSoyad", "SahipTelefon"]]),
        [ImportTable.Vaccinations] = new(
            ["AsiAdi", "UygulamaTarihi"],
            [["HayvanNo"], ["SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur"]]),
    };

    private static readonly Dictionary<ImportTable, string[]> SheetNames = new()
    {
        [ImportTable.Customers] = ["Musteriler", "Musteri", "Customers"],
        [ImportTable.Pets] = ["Hastalar", "Hasta", "Hayvanlar", "Hayvan", "Pets"],
        [ImportTable.Vaccinations] = ["Asilar", "Asi", "Vaccinations"],
    };

    public static IReadOnlyList<ImportCanonicalField> Fields(ImportTable table) => table switch
    {
        ImportTable.Customers => CustomerFields,
        ImportTable.Pets => PetFields,
        ImportTable.Vaccinations => VaccinationFields,
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    /// <summary>Şablonun sayfa adı (ilk ad).</summary>
    public static string SheetName(ImportTable table) => SheetNames[table][0];

    public static bool TryResolveSheet(string? sheetName, out ImportTable table)
    {
        var folded = ImportTextFolding.Fold(sheetName);
        foreach (var (candidate, names) in SheetNames)
        {
            if (names.Any(n => ImportTextFolding.Fold(n) == folded))
            {
                table = candidate;
                return true;
            }
        }

        table = default;
        return false;
    }

    public static bool IsCanonical(ImportTable table, string name)
        => Fields(table).Any(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Başlıklar için eşleme önerisi: önce şablon başlığı veya kanonik adla (eski şablon), sonra takma adla eşit olanlar;
    /// bir kanonik alan en çok bir sütuna, bir sütun en çok bir alana önerilir (başlık sırasında ilk gelen kazanır).
    /// </summary>
    public static IReadOnlyList<ImportColumnSuggestion> Suggest(ImportTable table, IReadOnlyList<string> headers)
    {
        var fields = Fields(table);
        var folded = headers.Select(ImportTextFolding.Fold).ToArray();
        var assigned = new (string Field, ImportSuggestionConfidence Confidence)?[headers.Count];
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        AssignPass(ImportSuggestionConfidence.Exact, f => [f.Header, f.Name]);
        AssignPass(ImportSuggestionConfidence.Alias, f => f.Aliases);

        return headers
            .Select((h, i) => assigned[i] is { } a
                ? new ImportColumnSuggestion(h, a.Field, a.Confidence)
                : new ImportColumnSuggestion(h, null, ImportSuggestionConfidence.None))
            .ToList();

        void AssignPass(ImportSuggestionConfidence confidence, Func<ImportCanonicalField, IEnumerable<string>> keys)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                if (assigned[i] is not null || folded[i].Length == 0)
                    continue;

                var match = fields.FirstOrDefault(f =>
                    !claimed.Contains(f.Name) && keys(f).Any(k => ImportTextFolding.Fold(k) == folded[i]));
                if (match is null)
                    continue;

                assigned[i] = (match.Name, confidence);
                claimed.Add(match.Name);
            }
        }
    }

    /// <summary>Eşlenmiş kanonik alanlara göre eksik zorunlu alanların okunabilir listesi (şablon başlıklarıyla; boşsa tam).</summary>
    public static IReadOnlyList<string> MissingRequired(ImportTable table, IEnumerable<string> mappedFields)
    {
        var mapped = mappedFields.ToHashSet(StringComparer.Ordinal);
        var requirement = Requirements[table];
        var missing = requirement.AllOf.Where(f => !mapped.Contains(f)).Select(f => Header(table, f)).ToList();

        if (requirement.AnyOf.Length > 0 && !requirement.AnyOf.Any(group => group.All(mapped.Contains)))
        {
            missing.Add(string.Join(" veya ", requirement.AnyOf.Select(g => string.Join(" + ", g.Select(f => Header(table, f))))));
        }

        return missing;
    }

    private static string Header(ImportTable table, string name)
        => Fields(table).First(f => string.Equals(f.Name, name, StringComparison.Ordinal)).Header;
}
