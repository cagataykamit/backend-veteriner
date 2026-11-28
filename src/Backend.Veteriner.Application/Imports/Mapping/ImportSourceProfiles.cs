using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>
/// Hazır eşlemenin bir satırı: kaynak programın dosyasındaki başlık → kanonik alan. <see cref="Verified"/> gerçek dışa aktarma
/// dosyasıyla denetlenince true yapılır; <see cref="Source"/> başlığın nereden bilindiğini yazar.
/// </summary>
public sealed record ImportProfileMapping(ImportTable Table, string Header, string Field, string Source, bool Verified = false);

/// <summary>Kaynak program profili; <see cref="Note"/> kullanıcıya ve destek ekibine profilin dayanağını anlatır.</summary>
public sealed record ImportSourceProfileDefinition(
    ImportSourceProfile Code,
    string DisplayName,
    string Note,
    IReadOnlyList<ImportProfileMapping> Mappings)
{
    /// <summary>Profilin tüm eşlemeleri gerçek dosyayla doğrulandıysa true; eşlemesiz profil doğrulanmış sayılmaz.</summary>
    public bool Verified => Mappings.Count > 0 && Mappings.All(m => m.Verified);
}

/// <summary>
/// Rakip programlardan gelen dosyalar için hazır sütun eşlemeleri — tek yer (TIER-021; IMPORT_API_CONTRACT.md §4.5). Eşleme
/// önerisi (<see cref="ImportCanonicalSchema.Suggest"/>) kanonik başlıktan sonra, genel takma adlardan önce bu listeye bakar;
/// kullanıcı öneriyi her zaman düzeltebilir. Kaynağı bulunamayan başlık buraya yazılmaz, genel takma ad listesine
/// (<see cref="ImportCanonicalSchema"/>) bırakılır. E-vet'in "Yedek Al" dosyası ve raporları görüldü (2026-11-11): yedek eşleme
/// adımına gelmeden kanonik tablolara çevrilir (<see cref="Reading.EVet.EVetBackupConverter"/>); buradaki E-vet satırları tek
/// sayfanın ayrıca CSV olarak yüklendiği durum içindir ve gerçek başlıklardır. Kolayvet'in liste dışa aktarmaları görüldü
/// (2026-11-28) ve aynı şekilde çevrilir (<see cref="Reading.Kolayvet.KolayvetExportConverter"/>); Kolayvet için hazır eşleme yoktur.
/// </summary>
public static class ImportSourceProfiles
{
    private const string EVetBackup = "E-vet Genel > Yedek Al Excel dosyası (gerçek dosyayla doğrulandı, 2026-11-11).";
    private const string EVetPatientReport = "E-vet Rapor > Genel > Hasta Listesi CSV'si (gerçek dosyayla doğrulandı, 2026-11-11).";

    public static readonly IReadOnlyList<ImportSourceProfileDefinition> All =
    [
        new(ImportSourceProfile.Generic, "Genel Excel / CSV",
            "Vetinity şablonu ya da başka bir programın dosyası. Yalnız genel eş anlamlı başlıklar kullanılır.",
            []),
        new(ImportSourceProfile.Kolayvet, "Kolayvet",
            "Kolayvet'in Müşteri Listesi, Hasta Listesi ve Aşı Takvimi ekranlarından \"Dışa Aktar\" ile indirdiğiniz Excel dosyaları doğrudan "
            + "okunur; Raporlar > Hasta Listesi (Detaylı) doğum tarihi ve mikroçipi tamamlar. Gerçek Kolayvet dosyalarıyla doğrulandı.",
            []),
        new(ImportSourceProfile.EVet, "E-vet",
            "E-vet'in Genel > Yedek Al Excel dosyası (müşteri, hasta, randevu, muayene) doğrudan okunur. Rapor > Genel > Hasta Listesi sahip "
            + "bağlantısını, Rapor > Resmi > Aşı Bilgi aşı seri numarasını tamamlar; Rapor > Depo > Ürün Listesi ürünleri getirir. "
            + "Gerçek E-vet dosyalarıyla doğrulandı.",
            [
                new(ImportTable.Customers, "Müşteriler", "AdSoyad", EVetBackup, Verified: true),
                new(ImportTable.Customers, "Protokol No", "MusteriNo", EVetBackup, Verified: true),
                new(ImportTable.Customers, "Gsm", "Telefon", EVetBackup, Verified: true),
                new(ImportTable.Customers, "Email", "Eposta", EVetBackup, Verified: true),
                new(ImportTable.Customers, "Bakiye", "AcikBakiye", EVetBackup, Verified: true),
                new(ImportTable.Customers, "Kimlik No", "KimlikNo", EVetBackup, Verified: true),
                new(ImportTable.Customers, "Vergi Dairesi", "VergiDairesi", EVetBackup, Verified: true),
                new(ImportTable.Customers, "Durum", "Durum", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Hastalar", "HayvanAdi", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Hasta Adı", "HayvanAdi", EVetPatientReport, Verified: true),
                new(ImportTable.Pets, "Protokol No", "HayvanNo", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Müşteriler", "SahipAdSoyad", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Hasta Türü", "Tur", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Hasta Irkı", "Irk", EVetPatientReport, Verified: true),
                new(ImportTable.Pets, "Irk", "Irk", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Cinsiyet", "Cinsiyet", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Doğum Tarihi", "DogumTarihi", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Çip No", "Mikrocip", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Kart No", "KarneNo", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Renk", "Renk", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Ağırlık", "Agirlik", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Alerjik Durumu", "Alerji", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Ölüm Zamanı", "VefatTarihi", EVetBackup, Verified: true),
                new(ImportTable.Pets, "Durum", "Durum", EVetBackup, Verified: true),
            ]),
    ];

    /// <summary>
    /// Sihirbazda sunulmayan ve yeni aktarmada kabul edilmeyen profiller. Tanım <see cref="All"/> içinde kalır: var olan partiler ve
    /// talepler adını ve eşlemesini okur. Kolayvet 2026-11-22'de gizlenmişti; gerçek dosya görülünce açıldı (2026-11-28).
    /// </summary>
    private static readonly HashSet<ImportSourceProfile> Hidden = [];

    /// <summary>"Hangi programdan geliyorsunuz?" seçenekleri: gizlenenler hariç.</summary>
    public static IEnumerable<ImportSourceProfileDefinition> Offered => All.Where(p => IsOffered(p.Code));

    /// <summary>Profil yeni aktarmada seçilebilir mi.</summary>
    public static bool IsOffered(ImportSourceProfile code) => !Hidden.Contains(code);

    public static ImportSourceProfileDefinition Get(ImportSourceProfile code) => All.First(p => p.Code == code);

    public static IEnumerable<ImportProfileMapping> MappingsFor(ImportSourceProfile code, ImportTable table)
        => Get(code).Mappings.Where(m => m.Table == table);

    /// <summary>İstek değeri (<c>Generic</c>, <c>Kolayvet</c>, <c>EVet</c>; büyük/küçük harf duyarsız); boş = <c>Generic</c>.</summary>
    public static bool TryParse(string? value, out ImportSourceProfile profile)
    {
        profile = ImportSourceProfile.Generic;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return Enum.TryParse(value.Trim(), ignoreCase: true, out profile)
               && Enum.IsDefined(profile)
               && !int.TryParse(value, out _);
    }
}
