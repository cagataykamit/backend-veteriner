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

    /// <summary>Seçilen kaynak programın hazır eşlemesi (<see cref="ImportSourceProfiles"/>; TIER-021, doğrulanmamış olabilir).</summary>
    Profile = 3,
}

public sealed record ImportColumnSuggestion(string Source, string? Suggested, ImportSuggestionConfidence Confidence);

/// <summary>Kanonik başlıklar, takma adlar ve zorunlu alan kuralları — tek yer (IMPORT_API_CONTRACT.md §4.2-4.3).</summary>
public static class ImportCanonicalSchema
{
    private const string PetNoHeader = "HastaNo";
    private const string PetNameHeader = "HastaAdi";
    private const string VaccinationPetLinkNote = "Evet (biri): HastaNo veya SahipAdSoyad + SahipTelefon + HastaAdi + Tur";
    private const string OwnerLinkNote = "Evet (biri): MusteriNo veya SahipAdSoyad + SahipTelefon";

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
        new("AcikBakiye", "Hayır", "Eski programdaki borç (TL). Pozitif tutar yeni müşteriye tek bir devir ücreti olarak açılır; negatif (alacak) aktarılmaz.", "1.250,00", true,
            "Açık Bakiye", "Bakiye", "Borç", "Borç Bakiyesi", "Kalan Borç", "Devir", "Devir Bakiyesi", "Cari Bakiye"),
        new("BakiyeTarihi", "Hayır", "Bakiyenin hangi tarihteki durum olduğu (devir ücreti açıklamasına yazılır).", "31.10.2026", true,
            "Bakiye Tarihi", "Devir Tarihi"),
        new("KimlikNo", "Hayır", "Fatura kimliği: 11 haneli T.C. kimlik no (bireysel) veya 10 haneli vergi no (kurumsal). Geçersizse fatura bilgisi boş kalır.", "10000000146", true,
            "Kimlik No", "TC Kimlik No", "T.C. Kimlik No", "TCKN", "T.C.", "Vergi No", "Vergi Numarası", "VKN"),
        new("VergiDairesi", "Hayır", "Kurumsal müşterinin vergi dairesi (en çok 100 karakter).", "Kadıköy", false,
            "Vergi Dairesi"),
        new("Durum", "Hayır", "Aktif veya Pasif. Pasif müşteri aktarılır ve hatırlatma istemiyor olarak işaretlenir.", "Aktif", false,
            "Müşteri Durumu"),
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
            "Cins", "Irkı", "Hasta Irkı", "Hayvan Irkı"),
        new("Cinsiyet", "Hayır", "Erkek / Dişi.", "Dişi", false,
            "Cinsiyeti", "Sex", "Gender"),
        new("DogumTarihi", "Hayır", "gg.aa.yyyy veya yyyy-aa-gg; gelecekte olamaz.", "15.03.2021", true,
            "Doğum Tarihi", "Doğum Tar", "D. Tarihi", "Doğum"),
        new("Kisir", "Hayır", "Evet / Hayır.", "Evet", false,
            "Kısır", "Kısırlaştırılmış", "Kısırlaştırma", "Kısırlık"),
        new("Mikrocip", "Hayır", "Yalnız rakam (genelde 15 hane); hücreyi METİN biçiminde tutun.", "985112345678901", true,
            "Mikroçip", "Mikroçip No", "Mikrocip No", "Çip", "Çip No", "Çip Numarası", "Chip", "Microchip"),
        new("Renk", "Hayır", "Katalogda yoksa nota eklenir.", "Sarı", false,
            "Tüy Rengi", "Renkler"),
        new("Agirlik", "Hayır", "Kilogram; ondalık virgül veya nokta.", "4,5", false,
            "Ağırlık", "Kilo", "Kg", "Ağırlık (kg)", "Kilo (kg)"),
        new("KarneNo", "Hayır", "Pasaport / karne numarası (en çok 50 karakter).", "K-77", true,
            "Karne No", "Karne", "Pasaport No", "Pasaport", "Kart No", "Küpe No"),
        new("Alerji", "Hayır", "Alerji bilgisi; hastaya alerji uyarısı olarak eklenir.", "Penisilin", false,
            "Alerjik Durumu", "Alerjiler", "Alerji Durumu"),
        new("Durum", "Hayır", "Aktif veya Vefat (Ölü). Boşsa aktif.", "Aktif", false,
            "Hasta Durumu", "Yaşam Durumu"),
        new("VefatTarihi", "Hayır", "Vefat tarihi (yalnız vefat eden hastada).", "02.09.2025", true,
            "Vefat Tarihi", "Ölüm Tarihi", "Ölüm Zamanı"),
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
            "Uygulama Tarihi", "Aşı Tarihi", "Tarih", "Uygulanma Tarihi", "Yapılış Tarihi", "Aşı Uygulama Tarihi"),
        new("SonrakiAsiTarihi", "Hayır", "Uygulama tarihinden sonra olmalı.", "10.04.2025", true,
            "Sonraki Aşı Tarihi", "Sonraki Aşı", "Bir Sonraki Aşı", "Tekrar Tarihi", "Rapel Tarihi", "Sonraki Tarih", "Vade"),
        new("Seri", "Hayır", "Seri / lot numarası (Not alanına eklenir).", "L-5521", true,
            "Seri No", "Lot", "Lot No"),
        new("Uygulayan", "Hayır", "Aşıyı yapan (Not alanına eklenir).", "Dr. Örnek", false,
            "Uygulayan Hekim", "Hekim", "Veteriner"),
        new("Not", "Hayır", "Serbest not.", "Doz 1", false,
            "Notlar", "Açıklama"),
    ];

    /// <summary>Hastaya bağlanan tabloların (klinik geçmiş, randevu) bağlantı alanları; aşı tablosuyla aynı tanım.</summary>
    private static ImportCanonicalField[] PetLinkFields() => VaccinationFields[..5];

    private static readonly ImportCanonicalField[] ClinicalRecordFields =
    [
        .. PetLinkFields(),
        new("Tarih", "Evet", "Kaydın tarihi: gg.aa.yyyy veya yyyy-aa-gg; gelecekte olamaz, 1990'dan eski olamaz.", "12.05.2024", true,
            "Muayene Tarihi", "Kayıt Tarihi", "İşlem Tarihi", "Geliş Tarihi", "Date"),
        new("KayitTuru", "Hayır", "Muayene, Kontrol, Ameliyat... (en çok 100 karakter).", "Muayene", false,
            "Kayıt Türü", "Kayıt Tipi", "Muayene Türü", "İşlem"),
        new("Sikayet", "Hayır*", "Şikâyet / geliş nedeni (en çok 2000 karakter). *Şikâyet, anamnez, bulgular, tanı, tedavi, reçete, lab sonucu, not, kilo veya ateşten en az biri dolu olmalı.", "İştahsızlık", false,
            "Şikayet", "Şikâyet", "Şikayeti", "Başvuru Nedeni", "Geliş Nedeni"),
        new("Anamnez", "Hayır*", "Öykü (en çok 4000 karakter).", "3 gündür yemiyor", false,
            "Öykü", "Hikaye", "Hikâye"),
        new("Bulgular", "Hayır*", "Muayene bulguları (en çok 8000 karakter).", "Ateş yüksek", false,
            "Bulgu", "Muayene Bulguları", "Klinik Bulgular", "Fizik Muayene", "Muayene"),
        new("Tani", "Hayır*", "Tanı (en çok 2000 karakter).", "Gastroenterit", false,
            "Tanı", "Teşhis", "Ön Tanı", "Kesin Tanı", "Tanılar"),
        new("Tedavi", "Hayır*", "Uygulanan tedavi / plan (en çok 4000 karakter).", "Serum, antibiyotik", false,
            "Uygulanan Tedavi", "Yapılan İşlem", "Yapılan İşlemler", "Plan"),
        new("Recete", "Hayır*", "Reçete metni (en çok 4000 karakter); reçete kaydı veya stok hareketi oluşmaz.", "Amoksisilin 2x1", false,
            "Reçete", "İlaçlar", "Reçete Edilen", "Verilen İlaçlar"),
        new("LabSonuc", "Hayır*", "Laboratuvar sonucu metni (en çok 4000 karakter).", "Hemogram normal", false,
            "Lab", "Laboratuvar", "Lab Sonucu", "Lab Sonuçları", "Tahlil", "Tahlil Sonucu", "Test Sonuçları"),
        new("Not", "Hayır*", "Serbest not (en çok 4000 karakter).", "Kontrole gelecek", false,
            "Notlar", "Açıklama"),
        new("Hekim", "Hayır", "Kaydı giren hekimin adı (serbest metin).", "Dr. Örnek", false,
            "Veteriner", "Veteriner Hekim", "Doktor", "Hekim Adı"),
        new("Kilo", "Hayır", "Kilogram; ondalık virgül veya nokta.", "4,5", false,
            "Ağırlık", "Kg", "Kilo (kg)", "Ağırlık (kg)"),
        new("Ates", "Hayır", "Vücut ısısı (°C, 25-45).", "38,6", false,
            "Ateş", "Vücut Isısı", "Sıcaklık", "Ateş (°C)"),
    ];

    private static readonly ImportCanonicalField[] AccountEntryFields =
    [
        new("MusteriNo", OwnerLinkNote, "Müşteri sayfasındaki MusteriNo ile aynı değer.", "M-1001", true,
            "Müşteri No", "Müşteri Numarası", "Müşteri Kodu", "Cari Kodu", "Cari No"),
        new("SahipAdSoyad", OwnerLinkNote, "Müşterinin adı soyadı.", "Ayşe Yılmaz", false,
            "Sahip Ad Soyad", "Sahip", "Müşteri", "Müşteri Adı", "Cari", "Cari Adı", "Ad Soyad"),
        new("SahipTelefon", OwnerLinkNote, "Müşterinin cep telefonu.", "05321234567", true,
            "Sahip Tel", "Müşteri Telefon", "Telefon", "Cep"),
        new("Tarih", "Evet", "Hareket tarihi: gg.aa.yyyy veya yyyy-aa-gg; gelecekte olamaz.", "05.03.2025", true,
            "İşlem Tarihi", "Hareket Tarihi", "Belge Tarihi", "Fiş Tarihi", "Fatura Tarihi"),
        new("IslemTuru", "Evet", "Borç (ücret, fatura, satış) veya Tahsilat (ödeme, makbuz).", "Tahsilat", false,
            "İşlem Türü", "Hareket Türü", "Hareket Tipi", "Borç/Alacak", "B/A", "Tip"),
        new("Tutar", "Evet", "TL; 1.250,50 veya 1250.50 biçimi; sıfırdan büyük.", "350,00", true,
            "Tutar (TL)", "Tutarı", "Toplam", "Meblağ"),
        new("Aciklama", "Hayır", "Açıklama (en çok 500 karakter).", "Aşı ücreti", false,
            "Açıklama", "Hizmet", "Ürün/Hizmet", "Not"),
        new("OdemeSekli", "Hayır", "Nakit, Kart... (bilgi; kasaya bağlanmaz).", "Nakit", false,
            "Ödeme Şekli", "Ödeme Türü", "Ödeme Tipi", "Ödeme Yöntemi"),
        new("BelgeNo", "Hayır", "Fatura / makbuz numarası (en çok 50 karakter).", "A-0042", true,
            "Belge No", "Fatura No", "Makbuz No", "Fiş No", "Evrak No"),
        new("HastaAdi", "Hayır", "İlgili hastanın adı (bilgi; hastaya bağlanmaz).", "Pamuk", false,
            "Hasta Adı", "Hayvan Adı", "Hasta", "Hayvan"),
    ];

    private static readonly ImportCanonicalField[] AppointmentFields =
    [
        .. PetLinkFields(),
        new("Tarih", "Evet", "Randevu tarihi: gg.aa.yyyy veya yyyy-aa-gg (saat de içerebilir). Geçmiş randevular atlanır.", "20.12.2026", true,
            "Randevu Tarihi", "Randevu Zamanı", "Tarih Saat"),
        new("Saat", "Hayır*", "SS:dd (24 saat). *Tarih saat taşımıyorsa zorunlu.", "14:30", true,
            "Randevu Saati", "Başlangıç", "Başlangıç Saati"),
        new("Sure", "Hayır", "Dakika (5-240); boşsa 30.", "30", true,
            "Süre", "Süre (dk)", "Dakika", "Süre (dakika)"),
        new("Hekim", "Hayır", "Hekimin adı; kullanıcıyla eşleşirse randevuya atanır, aksi nota yazılır.", "Dr. Örnek", false,
            "Veteriner", "Veteriner Hekim", "Doktor", "Hekim Adı"),
        new("Neden", "Hayır", "Randevu nedeni; tanımlı nedenle eşleşmezse nota yazılır.", "Aşı", false,
            "Randevu Nedeni", "Sebep", "Konu"),
        new("RandevuTuru", "Hayır", "Muayene, Aşı, Kontrol, Ameliyat, Bakım, Danışma.", "Kontrol", false,
            "Randevu Türü", "Randevu Tipi"),
        new("Not", "Hayır", "Randevu notu (en çok 2000 karakter).", "Aç gelsin", false,
            "Notlar", "Açıklama"),
    ];

    private static readonly ImportCanonicalField[] ProductFields =
    [
        new("Ad", "Evet", "Ürün veya hizmet adı (en çok 300 karakter).", "Kuduz aşısı", false,
            "Ürün Adı", "Hizmet Adı", "Ürün", "Hizmet", "Adı", "Stok Adı", "Malzeme", "Ürün/Hizmet"),
        new("UrunTuru", "Hayır", "Hizmet, İlaç, Aşı veya Diğer; boşsa Diğer.", "Aşı", false,
            "Ürün Türü", "Ürün Tipi", "Tür", "Tip"),
        new("Kategori", "Hayır", "Ayarlar'daki ürün kategorisiyle eşleşirse bağlanır.", "Aşılar", false,
            "Grup", "Ürün Grubu", "Kategori Adı"),
        new("Birim", "Hayır", "Satış birimi; boşsa Adet.", "Adet", false,
            "Birimi", "Ölçü Birimi"),
        new("SatisFiyati", "Hayır", "TL; boşsa 0.", "450,00", true,
            "Satış Fiyatı", "Fiyat", "Birim Fiyat", "Fiyatı", "Satış Fiyatı (TL)", "Fiyat (TL)"),
        new("Barkod", "Hayır", "En çok 100 karakter; hücreyi METİN biçiminde tutun.", "8690000000000", true,
            "Barkod No", "Barcode"),
        new("StokKodu", "Hayır", "Ürün kodu (en çok 100 karakter).", "ASI-001", true,
            "Stok Kodu", "Ürün Kodu", "Kod", "SKU"),
        new("Uretici", "Hayır", "Üretici / firma (en çok 200 karakter).", "Örnek İlaç A.Ş.", false,
            "Üretici", "Firma", "Marka"),
        new("Aciklama", "Hayır", "Açıklama (en çok 4000 karakter).", "Yıllık", false,
            "Açıklama", "Not"),
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
        [ImportTable.ClinicalRecords] = new(
            ["Tarih"],
            [["HayvanNo"], ["SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur"]]),
        [ImportTable.AccountEntries] = new(
            ["Tarih", "IslemTuru", "Tutar"],
            [["MusteriNo"], ["SahipAdSoyad", "SahipTelefon"]]),
        [ImportTable.Appointments] = new(
            ["Tarih"],
            [["HayvanNo"], ["SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur"]]),
        [ImportTable.Products] = new(["Ad"], []),
    };

    private static readonly Dictionary<ImportTable, string[]> SheetNames = new()
    {
        [ImportTable.Customers] = ["Musteriler", "Musteri", "Customers"],
        [ImportTable.Pets] = ["Hastalar", "Hasta", "Hayvanlar", "Hayvan", "Pets"],
        [ImportTable.Vaccinations] = ["Asilar", "Asi", "Vaccinations"],
        [ImportTable.ClinicalRecords] = ["KlinikGecmis", "Muayeneler", "Muayene", "KlinikKayitlar", "ClinicalRecords", "Examinations"],
        [ImportTable.AccountEntries] = ["HesapHareketleri", "Hareketler", "CariHareketler", "Tahsilatlar", "AccountEntries"],
        [ImportTable.Appointments] = ["Randevular", "Randevu", "Appointments"],
        [ImportTable.Products] = ["Urunler", "Urun", "Hizmetler", "UrunHizmet", "UrunlerHizmetler", "Products"],
    };

    public static IReadOnlyList<ImportCanonicalField> Fields(ImportTable table) => table switch
    {
        ImportTable.Customers => CustomerFields,
        ImportTable.Pets => PetFields,
        ImportTable.Vaccinations => VaccinationFields,
        ImportTable.ClinicalRecords => ClinicalRecordFields,
        ImportTable.AccountEntries => AccountEntryFields,
        ImportTable.Appointments => AppointmentFields,
        ImportTable.Products => ProductFields,
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
    /// Başlıklar için eşleme önerisi: önce şablon başlığı veya kanonik adla (eski şablon), sonra seçilen kaynak programın hazır
    /// eşlemesiyle (<see cref="ImportSourceProfiles"/>), en son genel takma adla eşit olanlar;
    /// bir kanonik alan en çok bir sütuna, bir sütun en çok bir alana önerilir (başlık sırasında ilk gelen kazanır).
    /// </summary>
    public static IReadOnlyList<ImportColumnSuggestion> Suggest(
        ImportTable table, IReadOnlyList<string> headers, ImportSourceProfile profile = ImportSourceProfile.Generic)
    {
        var fields = Fields(table);
        var folded = headers.Select(ImportTextFolding.Fold).ToArray();
        var assigned = new (string Field, ImportSuggestionConfidence Confidence)?[headers.Count];
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        AssignPass(ImportSuggestionConfidence.Exact, f => [f.Header, f.Name]);
        var profileHeaders = ImportSourceProfiles.MappingsFor(profile, table).ToLookup(m => m.Field, m => m.Header, StringComparer.Ordinal);
        AssignPass(ImportSuggestionConfidence.Profile, f => profileHeaders[f.Name]);
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
