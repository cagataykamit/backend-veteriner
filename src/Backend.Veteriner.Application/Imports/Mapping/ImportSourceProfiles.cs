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
/// kullanıcı öneriyi her zaman düzeltebilir. Elimizde bu programların gerçek dışa aktarma dosyası yoktur: her satır kamuya açık ya
/// da rakip incelemesinde görülen başlıktan gelir ve doğrulanmamıştır. Kaynağı bulunamayan başlık buraya yazılmaz, genel takma ad
/// listesine (<see cref="ImportCanonicalSchema"/>) bırakılır. Gerçek dosya gelince yalnız bu dosya düzeltilir.
/// </summary>
public static class ImportSourceProfiles
{
    private const string EVetCustomerList =
        "Rakip incelemesi: E-vet Müşteriler genel listesi ve Rapor > Müşteri Listesi sütunları (ürün deposu competitors/e-vet.md). "
        + "Yedek Al ekranı Müşteri Listesi'ni Excel olarak veriyor; dosyanın başlıkları görülmedi.";

    private const string EVetPatientList =
        "Rakip incelemesi: E-vet Rapor > Hasta Listesi ve Sahiplenme Listesi sütunları (ürün deposu competitors/e-vet.md). "
        + "Yedek Al ekranı Hasta Listesi'ni Excel olarak veriyor; dosyanın başlıkları görülmedi.";

    private const string EVetHbsList =
        "Rakip incelemesi: E-vet HBS e-Reçete ve ATS listesi sütunu (ürün deposu competitors/e-vet.md); hasta dışa aktarmasında görülmedi.";

    public static readonly IReadOnlyList<ImportSourceProfileDefinition> All =
    [
        new(ImportSourceProfile.Generic, "Genel Excel / CSV",
            "Vetinity şablonu ya da başka bir programın dosyası. Yalnız genel eş anlamlı başlıklar kullanılır.",
            []),
        new(ImportSourceProfile.Kolayvet, "Kolayvet",
            "Kolayvet sitesi hasta ve müşteri verisinin Excel ya da PDF olarak dışa alınabildiğini yazıyor; sütun adları kamuya açık değil "
            + "(kolayvet.com/ozellikler/ozellikler/gelismis-ice-disa-aktarma). Hazır eşleme yok, genel eş anlamlı başlıklar kullanılır. "
            + "Gerçek dosya görülünce eşleme eklenecek.",
            []),
        new(ImportSourceProfile.EVet, "E-vet",
            "Başlıklar E-vet ekranlarında görülen sütun adlarından alındı; gerçek dışa aktarma dosyasıyla doğrulanmadı. Aşı geçmişi için "
            + "E-vet dışa aktarması görülmedi, aşı sayfasında genel eş anlamlı başlıklar kullanılır.",
            [
                new(ImportTable.Customers, "Adı", "AdSoyad", EVetCustomerList),
                new(ImportTable.Customers, "Müşteri", "AdSoyad", EVetCustomerList),
                new(ImportTable.Customers, "Protokol No", "MusteriNo", EVetCustomerList),
                new(ImportTable.Customers, "Gsm", "Telefon", EVetCustomerList),
                new(ImportTable.Customers, "Email", "Eposta", EVetCustomerList),
                new(ImportTable.Pets, "Hasta Adı", "HayvanAdi", EVetPatientList),
                new(ImportTable.Pets, "Hasta", "HayvanAdi", EVetPatientList),
                new(ImportTable.Pets, "Protokol", "HayvanNo", EVetPatientList),
                new(ImportTable.Pets, "Tür", "Tur", EVetPatientList),
                new(ImportTable.Pets, "Hasta Irkı", "Irk", EVetPatientList),
                new(ImportTable.Pets, "Irk", "Irk", EVetPatientList),
                new(ImportTable.Pets, "Cinsiyet", "Cinsiyet", EVetPatientList),
                new(ImportTable.Pets, "Doğum Tarihi", "DogumTarihi", EVetPatientList),
                new(ImportTable.Pets, "Sahip", "SahipAdSoyad", EVetPatientList),
                new(ImportTable.Pets, "Gsm", "SahipTelefon", EVetPatientList),
                new(ImportTable.Pets, "Çip No", "Mikrocip", EVetHbsList),
            ]),
    ];

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
