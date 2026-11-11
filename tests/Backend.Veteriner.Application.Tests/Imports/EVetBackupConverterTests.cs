using System.Text;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SourceRow = Backend.Veteriner.Application.Imports.Validation.ImportSourceRow;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// E-vet "Genel &gt; Yedek Al" dosyası ve raporları (IMPORT_API_CONTRACT.md §13). Yapı gerçek dosyadan alındı; değerler uydurmadır
/// (gerçek kişisel veri yok).
/// </summary>
public sealed class EVetBackupConverterTests
{
    // 2026-10-14 12:00 İstanbul.
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static readonly object?[] CustomerHeader =
    [
        "Kayıt Tarihi", "Müşteriler", "Bakiye", "Protokol No", "Kimlik No", "Gsm", "Tel1", "Email", "Vergi Dairesi", "Vergi No",
        "Şehir", "İlçe", "Köy - Mahalle", "Adres", "Durum",
    ];

    private static readonly object?[] PetHeader =
    [
        "Müşteriler", "Hastalar", "Protokol No", "Çip No", "Kart No", "Doğum Tarihi", "Hasta Türü", "Irk", "Cinsiyet", "Renk",
        "Ağırlık", "Kalıtsal Hastalık", "Ölüm Zamanı", "Pasif Nedeni", "Durum",
    ];

    private static readonly object?[] TaskHeader =
        ["Müşteriler", "Hastalar", "Görev Tipi", "Başlangıç Tarihi", "Aşı Paketi", "Veteriner", "Bilgi", "Açıklama", "Durum"];

    private static readonly object?[] ExaminationHeader =
    [
        "Tarih", "Hasta Sahibi", "Hasta Adı", "Veteriner Hekim", "Ağırlık", "Vücut Sıcaklığı", "Semptom", "Hastalık Teşhisi",
        "Anamnez", "Tedavi Şekli", "Açıklama", "Diğer Parametreler",
    ];

    private static readonly DateTime Registered = new(2024, 1, 1);

    private static readonly byte[] BackupBytes = ImportTestFiles.Xlsx(
        ("Müşteri Listesi",
        [
            CustomerHeader,
            [Registered, "Ayşe Yılmaz", 1250.0000001, 101, "10000000146", "905321234567", null, null, null, null, "İstanbul", "Kadıköy", "Moda", "Örnek Sok. 1", "Aktif"],
            [Registered, "Can Demir", 0.0000001, 102, null, null, "905339876543", null, "Kadıköy", "1234567890", null, null, null, null, "Pasif"],
            [Registered, "Ayşe Yılmaz", -50.5, 103, null, "905551112233", null, null, null, null, null, null, null, null, "Aktif"],
        ]),
        ("Hasta Listesi",
        [
            PetHeader,
            ["Ayşe Yılmaz", "Pamuk", 201, "985000000000001", null, new DateTime(2020, 1, 1), "Kedi", "Tekir", "Kısırlaştırılmış Dişi", "Beyaz", 4.2000001, "Böbrek", null, null, "Aktif"],
            ["Can Demir", "Karabaş", 202, "985000000000001", "TR001", null, "Köpek", "Kangal", "Erkek", null, 0d, null, new DateTime(2025, 9, 2), "Yaşlılık", "Ölü"],
            ["Ayşe Yılmaz", "Boncuk", 203, null, null, null, "Köpek", "Melez", "Kısır", null, null, null, null, "Sahiplendirildi", "Pasif"],
            ["Ayşe Yılmaz", "Pamuk", 204, null, null, null, "Kedi", "Tekir", "Dişi", null, null, null, null, null, "Aktif"],
            ["Ayşe Yılmaz", "Tarçın", 205, "abc 123", null, null, "Kedi", "Tekir", "Bilinmiyor", null, null, null, null, null, "Aktif"],
        ]),
        ("Randevu Listesi",
        [
            TaskHeader,
            ["Ayşe Yılmaz", "Tarçın", "Tamamlandı", new DateTime(2025, 10, 1, 10, 0, 0), "KEDİ KARMA 1", "Dr. A", null, "İlk doz", "Tamamlandı"],
            ["Ayşe Yılmaz", "Tarçın", "Aşılama", new DateTime(2025, 10, 22, 10, 0, 0), "KEDİ KARMA 2", "Dr. A", null, null, "Yapılacak"],
            ["Ayşe Yılmaz", "Tarçın", "Aşılama", new DateTime(2026, 11, 20, 10, 30, 0), "KUDUZ", "Dr. A", null, null, "Yapılacak"],
            ["Ayşe Yılmaz", "Tarçın", "Kontrol Muayanesi", new DateTime(2026, 10, 20, 14, 0, 0), null, "Dr. B", null, "Dikiş kontrol", "Yapılacak"],
            ["Ayşe Yılmaz", "Tarçın", "Kontrol Muayanesi", new DateTime(2025, 1, 1, 14, 0, 0), null, "Dr. B", null, null, "Tamamlandı"],
            ["Ayşe Yılmaz", null, "BORÇ HATIRLATMA", new DateTime(2026, 11, 1), null, "Dr. B", null, null, "Yapılacak"],
            ["Can Demir", "Karabaş", "Maaş", new DateTime(2026, 11, 1), null, "Dr. B", null, null, "Yapılacak"],
            ["Ayşe Yılmaz", "Boncuk", "Aşılama", new DateTime(2026, 12, 1, 9, 0, 0), "KUDUZ", "Dr. A", null, null, "Yapılacak"],
            ["Ayşe Yılmaz", "Tarçın", "Aşılama", new DateTime(2025, 5, 1, 9, 0, 0), "MANTAR 1", "Dr. A", null, null, "Yapılacak"],
            ["Ayşe Yılmaz", "Tarçın", "Tamamlandı", new DateTime(2025, 10, 1, 10, 20, 0), "İÇ+DIŞ PARAZİT", "Dr. A", null, null, "Tamamlandı"],
            ["Can Demir", "Karabaş", "Aşılama", new DateTime(2024, 3, 1, 10, 0, 0), "KUDUZ", "Dr. A", null, null, "Tamamlandı"],
            ["Can Demir", "Karabaş", "Aşılama", new DateTime(2024, 3, 2, 10, 0, 0), "KUDUZ", "Dr. A", null, null, "İptal"],
        ]),
        ("Muayene Listesi",
        [
            ExaminationHeader,
            [new DateTime(2025, 6, 1, 11, 0, 0), "Ayşe Yılmaz", "Tarçın", "Dr. A", 4.25, "38.50000000", "Kusma", "Gastrit", null, "Serum", null,
                "Genel Durum: NORMAL, Solunum Sayısı: -, Kapiller Dolum: 2.00"],
            [new DateTime(2025, 6, 2, 11, 0, 0), "Ayşe Yılmaz", "Pamuk", "Dr. A", 0d, null, "Öksürük", null, null, null, null, null],
            [new DateTime(2025, 6, 3, 11, 0, 0), "Ayşe Yılmaz", "Tarçın", "Dr. A", 3.1, null, null, null, null, null, null,
                "Genel Durum: -, Solunum Sayısı: -"],
        ]));

    private const string PatientReport =
        ";;;;;;;14.10.2026;\n"
        + "AKTİF MÜŞTERİ VE HASTA LİSTESİ;;;;;;;;\n"
        + "#;Protokol;Müşteri;Kayıt Tarihi;Kart No;;Gsm;;\n"
        + "1;101;Ayşe Yılmaz;01.01.2024;;;905321234567;;\n"
        + ";Hasta Adı;Protokol;Tür;Hasta Irkı;Cinsiyet;Doğum Tarihi;Kayıt Tarihi;\n"
        + "Pamuk;201;;Kedi;Tekir;Kısırlaştırılmış Dişi;01.01.2020;01.01.2024;\n"
        + "Boncuk;203;;Köpek;Melez;Kısır;;01.01.2024;\n"
        + "Tarçın;205;;Kedi;Tekir;Bilinmiyor;;01.01.2024;\n"
        + "2;103;Ayşe Yılmaz;01.02.2024;;;905551112233;;\n"
        + ";Hasta Adı;Protokol;Tür;Hasta Irkı;Cinsiyet;Doğum Tarihi;Kayıt Tarihi;\n"
        + "Pamuk;204;;Kedi;Tekir;Dişi;;01.02.2024;\n";

    private const string VaccineReport =
        ";;;;;;;;;;;14.10.2026;;\n"
        + "AŞI BİLGİ RAPORU;;;;;;;;;;;;;\n"
        + "#;Tarih;Hasta Sahibi;T.C.;Hasta;Tür;Çip No;Ürün;Seri No;S.K.T;Birim;Miktar;Fiyat;Toplam\n"
        + "1;01.10.2025 10:05;Ayşe Yılmaz;;Tarçın;Kedi;-;Felocell Cvr (Kedi Karma);SER-1;;Doz;1,00;100,00;100,00\n"
        + "2;01.03.2024 10:00;Can Demir;;Karabaş;Köpek;-;Rabisin ( Kuduz );SER-2;;Doz;1,00;100,00;100,00\n"
        + "3;05.05.2025 11:00;Ayşe Yılmaz;;Doğrudan Satış;-;-;Rabisin;SER-3;;Doz;1,00;100,00;100,00\n";

    private const string ProductReport =
        ";;;;;;;;14.10.2026;;\n"
        + "Ürün Listesi;;;;;;;;;;\n"
        + ";#;Ürün;Barkod-1;Barkod-2;Alış KDV;Alış Fiyatı;Satış KDV;Satış Fiyatı;Durum;\n"
        + "Aşı;;;;;;;;;;\n"
        + ";;;;;;;;;;\n"
        + ";1;Kuduz Aşısı;8690000000001;;10;₺100,00;10;₺250,00;;[+]\n"
        + "MSD;;;;;;;;;;\n"
        + ";2;Nobivac;8690000000002;X2;10;₺100,00;10;₺1.250,50;;[+]\n"
        + ";3;Eski Aşı;;;10;₺1,00;10;₺2,00;;[-]\n"
        + "Hizmet;;;;;;;;;;\n"
        + ";;;;;;;;;;\n"
        + ";4;Muayene;;;0;₺0,00;20;₺500,00;;[+]\n";

    private static ImportUploadReader Reader(Action<ImportFileLimits>? configure = null)
        => new(Options.Create(ImportTestFiles.Limits(configure)), new FakeTimeProvider(Now));

    private static ImportUploadedFile Csv(string text, string name) => ImportTestFiles.Csv(text, name, bom: true);

    private static ImportFileSet Files(bool patientReport = true, bool vaccineReport = true, bool products = true)
        => new(
            ImportTestFiles.XlsxFile(BackupBytes, "backup.xlsx"),
            null,
            null,
            null,
            ProductsCsv: products ? Csv(ProductReport, "urunler.csv") : null,
            EVetPatientReport: patientReport ? Csv(PatientReport, "hasta.csv") : null,
            EVetVaccineReport: vaccineReport ? Csv(VaccineReport, "asi.csv") : null);

    private static ImportReadResult Read(ImportFileSet files)
    {
        var reader = Reader();
        var prepared = reader.Prepare(files, null, null, ImportSourceProfile.EVet);
        prepared.IsSuccess.Should().BeTrue(prepared.IsSuccess ? null : prepared.Error.Message);
        var read = reader.Read(prepared.Value!, trialTenant: false);
        read.IsSuccess.Should().BeTrue(read.IsSuccess ? null : read.Error.Message);
        return read.Value!;
    }

    private static List<Dictionary<string, string?>> Rows(ImportReadResult result, ImportTable table)
    {
        var source = result.Tables.Single(t => t.Table == table);
        return source.Rows
            .Select(r => source.Headers.Select((h, i) => (h, v: r.Cells[i])).ToDictionary(x => x.h, x => x.v))
            .ToList();
    }

    // ================================================================ Dosya kabulü

    [Fact]
    public void Backup_Should_Be_Converted_To_Canonical_Tables_In_Validation_Order()
    {
        var result = Read(Files());

        result.Tables.Select(t => t.Table).Should().Equal(
            ImportTable.Customers, ImportTable.Pets, ImportTable.Vaccinations, ImportTable.ClinicalRecords, ImportTable.Appointments,
            ImportTable.Products);
        result.Tables.SelectMany(t => t.Headers).Should().OnlyContain(h =>
            result.Tables.Any(t => ImportCanonicalSchema.IsCanonical(t.Table, h)));
        result.Warnings.Should().OnlyContain(w => w.Code == "Imports.EVetConversion");
    }

    [Fact]
    public void EVet_Reports_Should_Be_Rejected_Without_EVet_Profile_Or_Backup()
    {
        var reader = Reader();
        reader.Prepare(Files(), null, null, ImportSourceProfile.Generic).Error.Code.Should().Be("Imports.UnsupportedFile");

        var withoutBackup = new ImportFileSet(null, Csv("AdSoyad\nAyşe", "m.csv"), null, null, EVetPatientReport: Csv(PatientReport, "h.csv"));
        reader.Prepare(withoutBackup, null, null, ImportSourceProfile.EVet).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Workbook_With_Products_Csv_Should_Be_Accepted_Only_For_EVet()
    {
        var reader = Reader();
        var files = Files(patientReport: false, vaccineReport: false);

        reader.Prepare(files, null, null, ImportSourceProfile.Generic).Error.Code.Should().Be("Imports.UnsupportedFile");
        reader.Prepare(files, null, null, ImportSourceProfile.EVet).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Content_Hash_Should_Include_The_Reports()
    {
        var reader = Reader();
        var full = reader.Prepare(Files(), null, null, ImportSourceProfile.EVet).Value!.ContentHash;
        var backupOnly = reader.Prepare(Files(false, false, false), null, null, ImportSourceProfile.EVet).Value!.ContentHash;

        full.Should().NotBe(backupOnly);
        backupOnly.Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(BackupBytes)));
    }

    [Fact]
    public void Unrecognized_Patient_Report_Should_Be_Rejected()
    {
        var reader = Reader();
        var files = Files() with { EVetPatientReport = Csv("a;b;c\n1;2;3\n", "h.csv") };

        reader.Read(reader.Prepare(files, null, null, ImportSourceProfile.EVet).Value!, false).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Template_Workbook_Should_Still_Be_Read_Generically_With_EVet_Profile()
    {
        var reader = Reader();
        var files = new ImportFileSet(ImportTestFiles.XlsxFile(ImportTestFiles.Xlsx(("Musteriler", [["AdSoyad"], ["Ayşe Yılmaz"]]))), null, null, null);

        var read = reader.Read(reader.Prepare(files, null, null, ImportSourceProfile.EVet).Value!, false);

        read.Value!.Tables.Single().Headers.Should().Equal("AdSoyad");
    }

    [Fact]
    public void Converted_Table_Above_Row_Limit_Should_Be_Rejected()
    {
        var reader = Reader(l => l.MaxRowsPerTable = 3);

        var read = reader.Read(reader.Prepare(Files(), null, null, ImportSourceProfile.EVet).Value!, false);

        read.Error.Code.Should().Be("Imports.TooManyRows");
    }

    // ================================================================ Müşteri

    [Fact]
    public void Customer_Should_Get_Protocol_Address_Rounded_Balance_And_Identity()
    {
        var customers = Rows(Read(Files()), ImportTable.Customers);

        customers[0]["MusteriNo"].Should().Be("101");
        customers[0]["Adres"].Should().Be("Örnek Sok. 1, Moda, Kadıköy / İstanbul");
        customers[0]["AcikBakiye"].Should().Be("1250.00");
        customers[0]["KimlikNo"].Should().Be("10000000146");
        customers[1]["AcikBakiye"].Should().BeNull("kuruş altı kayan nokta artığı sıfırdır");
        customers[1]["Telefon"].Should().Be("905339876543", "Gsm boşsa Tel1");
        customers[1]["KimlikNo"].Should().Be("1234567890", "vergi numarası kimlik numarasından önce gelir");
        customers[1]["VergiDairesi"].Should().Be("Kadıköy");
        customers[1]["Durum"].Should().Be("Pasif");
        customers[2]["AcikBakiye"].Should().Be("-50.50");
    }

    // ================================================================ Hasta

    [Fact]
    public void Pet_Owner_Should_Come_From_Patient_Report_Then_From_Unique_Owner_Name()
    {
        var pets = Rows(Read(Files()), ImportTable.Pets);

        pets.Single(p => p["HayvanNo"] == "201")["MusteriNo"].Should().Be("101");
        pets.Single(p => p["HayvanNo"] == "204")["MusteriNo"].Should().Be("103", "aynı adlı iki müşteriyi rapor ayırır");
        pets.Single(p => p["HayvanNo"] == "202")["MusteriNo"].Should().Be("102", "raporda yok, sahip adı tek");
    }

    [Fact]
    public void Without_Patient_Report_Same_Named_Owners_Should_Stay_Unlinked()
    {
        var result = Read(Files(patientReport: false));
        var pets = Rows(result, ImportTable.Pets);

        pets.Single(p => p["HayvanNo"] == "201")["MusteriNo"].Should().BeNull();
        pets.Single(p => p["HayvanNo"] == "201")["SahipAdSoyad"].Should().Be("Ayşe Yılmaz");
        pets.Single(p => p["HayvanNo"] == "202")["MusteriNo"].Should().Be("102");
        result.Warnings.Should().Contain(w => w.Message.Contains("Hasta Listesi raporu yüklenmedi"));
    }

    [Fact]
    public void Gender_Cell_Should_Be_Split_Into_Gender_And_Neutered()
    {
        var pets = Rows(Read(Files()), ImportTable.Pets).ToDictionary(p => p["HayvanNo"]!);

        (pets["201"]["Cinsiyet"], pets["201"]["Kisir"]).Should().Be(("Dişi", "Evet"));
        (pets["202"]["Cinsiyet"], pets["202"]["Kisir"]).Should().Be(("Erkek", null));
        (pets["203"]["Cinsiyet"], pets["203"]["Kisir"]).Should().Be((null, "Evet"));
        (pets["205"]["Cinsiyet"], pets["205"]["Kisir"]).Should().Be((null, null));
    }

    [Fact]
    public void Deceased_And_Passive_Pets_Should_Be_Marked_And_Noted()
    {
        var pets = Rows(Read(Files()), ImportTable.Pets).ToDictionary(p => p["HayvanNo"]!);

        pets["202"]["Durum"].Should().Be("Vefat");
        pets["202"]["VefatTarihi"].Should().Be("2025-09-02");
        pets["202"]["Not"].Should().Contain("Eski program vefat notu: Yaşlılık");
        pets["203"]["Durum"].Should().BeNull("pasifin karşılığı yok: aktif kalır");
        pets["203"]["Not"].Should().Be("Eski programda pasif. Sahiplendirildi");
        pets["201"]["Not"].Should().Be("Kalıtsal hastalık: Böbrek");
    }

    [Fact]
    public void Duplicate_Or_Non_Numeric_Chip_Should_Move_To_Notes_And_Zero_Weight_Be_Empty()
    {
        var pets = Rows(Read(Files()), ImportTable.Pets).ToDictionary(p => p["HayvanNo"]!);

        pets["201"]["Mikrocip"].Should().Be("985000000000001");
        pets["202"]["Mikrocip"].Should().BeNull("aynı çip başka hastada");
        pets["202"]["Not"].Should().Contain("Mikroçip (eski program): 985000000000001");
        pets["205"]["Mikrocip"].Should().BeNull();
        pets["205"]["Not"].Should().Contain("Mikroçip (eski program): abc 123");
        pets["201"]["Agirlik"].Should().Be("4.2");
        pets["202"]["Agirlik"].Should().BeNull();
        pets["202"]["KarneNo"].Should().Be("TR001");
    }

    // ================================================================ Randevu → aşı / randevu

    [Fact]
    public void Completed_Vaccine_Tasks_Should_Become_Vaccinations_With_Due_Date_From_Pending_Series_Task()
    {
        var vaccinations = Rows(Read(Files()), ImportTable.Vaccinations);

        vaccinations.Should().HaveCount(3);
        var karma = vaccinations.Single(v => v["AsiAdi"] == "KEDİ KARMA 1");
        karma["HayvanNo"].Should().Be("205");
        karma["UygulamaTarihi"].Should().Be("2025-10-01");
        karma["SonrakiAsiTarihi"].Should().Be("2025-10-22", "aynı serinin (karma) yapılacak görevi");
        karma["Uygulayan"].Should().Be("Dr. A");
        vaccinations.Single(v => v["AsiAdi"] == "İÇ+DIŞ PARAZİT")["SonrakiAsiTarihi"].Should().BeNull();
    }

    [Fact]
    public void Vaccine_Report_Should_Add_Serial_By_Single_Match_Or_By_Product_Series()
    {
        var vaccinations = Rows(Read(Files()), ImportTable.Vaccinations);

        var karma = vaccinations.Single(v => v["AsiAdi"] == "KEDİ KARMA 1");
        karma["Seri"].Should().Be("SER-1", "aynı gün iki aşı var; ürün adı karma serisini içeriyor");
        karma["Not"].Should().Be("Ürün: Felocell Cvr (Kedi Karma); İlk doz");
        vaccinations.Single(v => v["AsiAdi"] == "İÇ+DIŞ PARAZİT")["Seri"].Should().BeNull();
        vaccinations.Single(v => v["HayvanNo"] == "202")["Seri"].Should().Be("SER-2");
    }

    [Fact]
    public void Future_Tasks_Should_Become_Appointments_And_Others_Skipped()
    {
        var result = Read(Files());
        var appointments = Rows(result, ImportTable.Appointments);

        appointments.Should().HaveCount(2);
        var vaccine = appointments.Single(a => a["RandevuTuru"] == "Aşı");
        (vaccine["HayvanNo"], vaccine["Tarih"], vaccine["Saat"], vaccine["Not"]).Should().Be(("205", "2026-11-20", "10:30", "Aşı: KUDUZ"));
        var checkup = appointments.Single(a => a["RandevuTuru"] == "Kontrol");
        (checkup["Neden"], checkup["Hekim"], checkup["Not"]).Should().Be(("Kontrol Muayanesi", "Dr. B", "Dikiş kontrol"));

        var skipped = result.Warnings.Single(w => w.Message.Contains("görev aktarılmayacak")).Message;
        skipped.Should().Contain("hastası olmayan: 1").And.Contain("hasta işi olmayan türde").And.Contain("iptal edilmiş: 1")
            .And.Contain("vefat ya da pasif hastaya ait yapılacak aşı: 1").And.Contain("hiç uygulanmamış ve tarihi geçmiş aşı: 1");
    }

    // ================================================================ Muayene

    [Fact]
    public void Examination_Should_Link_By_Owner_And_Pet_Name_And_Map_Fields()
    {
        var records = Rows(Read(Files()), ImportTable.ClinicalRecords);

        var first = records[0];
        (first["HayvanNo"], first["Tarih"], first["KayitTuru"], first["Hekim"]).Should().Be(("205", "2025-06-01", "Muayene", "Dr. A"));
        (first["Kilo"], first["Ates"], first["Sikayet"], first["Tani"], first["Tedavi"]).Should().Be(("4.25", "38.5", "Kusma", "Gastrit", "Serum"));
        first["Bulgular"].Should().Be("Genel Durum: NORMAL; Kapiller Dolum: 2.00", "boş (-) parametreler atılır");
        records[1]["HayvanNo"].Should().BeNull("aynı sahip adı + hasta adı iki hastada: belirsiz, doğrulama hata verir");
        records[1]["Kilo"].Should().BeNull();
        (records[2]["Kilo"], records[2]["Bulgular"]).Should().Be(("3.1", null));
    }

    // ================================================================ Ürün listesi

    [Fact]
    public void Product_Report_Should_Resolve_Groups_Prices_And_Skip_Passive()
    {
        var result = Read(Files());
        var products = Rows(result, ImportTable.Products);

        products.Select(p => (p["Ad"], p["UrunTuru"], p["Kategori"], p["SatisFiyati"])).Should().Equal(
            ("Kuduz Aşısı", "Aşı", "Aşı", "₺250,00"),
            ("Nobivac", "Aşı", "Aşı", "₺1.250,50"),
            ("Muayene", "Hizmet", "Hizmet", "₺500,00"));
        products[1]["Aciklama"].Should().Be("Grup: MSD; Barkod 2: X2");
        products[0]["Barkod"].Should().Be("8690000000001");
        result.Warnings.Should().Contain(w => w.Message.Contains("1 pasif ürün"));
    }

    // ================================================================ Doğrulama motoru ile uçtan uca

    [Fact]
    public void Converted_Tables_Should_Validate_With_Exact_Mapping()
    {
        var result = Read(Files());
        Guid cat = Guid.NewGuid(), dog = Guid.NewGuid(), karma = Guid.NewGuid(), kuduz = Guid.NewGuid();
        var catalogs = new ImportCatalogs(
            Species: [new(cat, "Kedi", null, null), new(dog, "Köpek", null, null)],
            Breeds: [],
            Colors: [],
            Vaccines: [new(karma, "Karma", "MIXED", null), new(kuduz, "Kuduz", "RABIES", null)]);
        var tables = result.Tables
            .Where(t => t.Table != ImportTable.Products)
            .ToDictionary(
                t => t.Table,
                t => (IReadOnlyList<SourceRow>)t.Rows.Select(r => SourceRow.FromJson(r.RowNumber, ImportSourceRowJson.Serialize(t.Headers, r.Cells))).ToList());
        var mapping = new ImportMappingDocument
        {
            Columns = result.Tables.ToDictionary(t => ImportCanonicalNames.Key(t.Table), t => t.Headers.ToDictionary(h => h, h => h)),
        };

        var output = ImportValidationEngine.Run(new ImportValidationInput(
            tables, mapping, new ImportOptionsDocument(ImportOptionsDocument.Match), catalogs, ImportExistingData.Empty, Now));

        ImportRowResult Row(ImportTable table, int row) => output.Rows.Single(r => r.Table == table && r.RowNumber == row);

        var deceased = (ImportNormalizedPet)Row(ImportTable.Pets, 3).Normalized;
        (deceased.Deceased, deceased.DeceasedOn).Should().Be((true, new DateOnly(2025, 9, 2)));
        var pasif = (ImportNormalizedCustomer)Row(ImportTable.Customers, 3).Normalized;
        (pasif.RemindersOptedOut, pasif.TaxNumber, pasif.TaxOffice).Should().Be((true, "1234567890", "Kadıköy"));
        ((ImportNormalizedCustomer)Row(ImportTable.Customers, 2).Normalized).NationalId.Should().Be("10000000146");

        var karmaRow = (ImportNormalizedVaccination)Row(ImportTable.Vaccinations, 2).Normalized;
        (karmaRow.VaccineDefinitionId, karmaRow.DueAt, karmaRow.Notes).Should().Be(
            (karma, new DateOnly(2025, 10, 22), "Seri: SER-1; Uygulayan: Dr. A; Ürün: Felocell Cvr (Kedi Karma); İlk doz"));

        Row(ImportTable.ClinicalRecords, 3).Issues.Should().Contain(i => i.Code == ImportRules.PetNotFound.Code, "belirsiz eşleşme hatadır");
        Row(ImportTable.ClinicalRecords, 4).Status.Should().NotBe(ImportRowValidationStatus.Error, "yalnız kilo taşıyan kayıt boş değildir");
        output.Rows.Where(r => r.Table == ImportTable.Appointments).Should().OnlyContain(r => r.Status != ImportRowValidationStatus.Error);
    }
}
