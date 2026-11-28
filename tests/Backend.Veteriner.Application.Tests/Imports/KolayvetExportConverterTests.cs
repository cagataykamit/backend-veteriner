using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Application.Imports.Reading.Kolayvet;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// Kolayvet liste dışa aktarmaları (IMPORT_API_CONTRACT.md §15). Yapı gerçek dosyalardan alındı (2026-11-28: 1. satır sayfa başlığı,
/// sütun adları 2. satırda, telefon hücresinde numara iki kez ve düğme metni, aşı tarihi Türkçe ay adıyla); değerler uydurmadır.
/// </summary>
public sealed class KolayvetExportConverterTests
{
    // 2026-11-28 12:00 İstanbul.
    private static readonly DateTime Now = new(2026, 11, 28, 9, 0, 0, DateTimeKind.Utc);

    private static readonly object?[] CustomerHeader = ["Müşteri NO", "Müşteri Adı", "Cep Telefonu", "Kalan bakiye", "TC Kimlik No"];

    private static readonly object?[] PetHeader =
        ["Müşteri NO", "Müşteri Adı", "Hasta NO", "Hasta Adı", "Türü", "Cinsiyet", "Irkı", "Aşı Takvimi", "Mikroçip"];

    private static readonly object?[] VaccineHeader =
    [
        "Müşteri NO", "Müşteri Adı", "Müşteri Telefon", "TC Kimlik No", "Hasta NO", "Hasta Adı", "Hasta Tür", "Aşı", "Aşı Tarihi",
        "Yapılma durumu", "Yapılma Tarihi", "Seri No", "Lokasyon",
    ];

    private static readonly object?[] ReportHeader = ["Müşteri Bilgileri", "Hasta Bilgileri", "Ek Bilgiler"];

    private static readonly byte[] CustomersBytes = Sheet("Müşteri Listesi | Kolayvet", CustomerHeader,
    [
        [1001, "Ayşe Yılmaz", "0532 111 22 330532 111 22 33 Hemen Ara WhatsApp'tan Yazın", 0d, 10000000146L],
        [1002, "Can Demir", "0533 444 55 660533 444 55 66 Hemen Ara WhatsApp'tan Yazın", 150.5, null],
        [1003, "Ayşe Yılmaz", "0544 777 88 990544 777 88 99 Hemen Ara WhatsApp'tan Yazın", -20d, null],
    ]);

    private static readonly byte[] PetsBytes = Sheet("Hasta Listesi | Kolayvet", PetHeader,
    [
        [1001, "Ayşe Yılmaz", 2001, "Pamuk", "Kedi", "Dişi", "Tekir", "Oluşturuldu", null],
        [1002, "Can Demir", 2002, "Karabaş", "Köpek", "Erkek", "Kangal", "Oluşturuldu", null],
        [1003, "Ayşe Yılmaz", 2003, "Pamuk", "Köpek", "Erkek", "Melez", "Oluşturuldu", null],
        [1002, "Can Demir", 2004, "Duman", "Kedi", "Erkek", "Van", "Oluşturuldu", "985000000000099"],
    ]);

    private static readonly byte[] VaccinesBytes = Sheet("Aşı Takvimi | Kolayvet", VaccineHeader,
    [
        // Uygulanmış karma 1; aynı serinin yapılacak karma 2'si sonraki aşı tarihi olur.
        [1001, "Ayşe Yılmaz", "05321112233", "10000000146", 2001, "Pamuk", "null / null", "Karma 1 Aşısı", "01 Ekim 2026", "Yapıldı", "2026-10-03", null, "Kadıköy - İstanbul"],
        [1001, "Ayşe Yılmaz", "05321112233", "10000000146", 2001, "Pamuk", "null / null", "Karma 2 Aşısı", "22 Ekim 2026", "Yapılmadı", "null", null, null],
        // Hiç uygulanmamış, gelecekte: randevu (saat 10:00).
        [1001, "Ayşe Yılmaz", "05321112233", "10000000146", 2001, "Pamuk", "null / null", "Kuduz", "15 Aralık 2026", "Yapılmadı", "null", null, null],
        // Hiç uygulanmamış, geçmişte: aktarılmaz.
        [1002, "Can Demir", "05334445566", null, 2002, "Karabaş", "null / null", "Bronchine 1", "02 Mart 2025", "Yapılmadı", "null", null, null],
        // Yapılma tarihi boş: planlanan tarih yazılır, notta söylenir.
        [1002, "Can Demir", "05334445566", null, 2002, "Karabaş", "null / null", "Kuduz Aşısı", "09 Eylül 2025", "Yapıldı", "null", "L-77", null],
    ]);

    private static readonly byte[] ReportBytes = Sheet("Yeni Hasta Listesi | Kolayvet", ReportHeader,
    [
        // Gerçek dosyada ad ile "Tel:" ve hasta adı ile tür arasında satır sonu var. Aynı adlı iki müşteri: telefon ayırır.
        ["Ayşe Yılmaz\r\nTel: 0532 111 22 33T.C.: 10000000146", "PAMUK\r\nKedi / Tekir  Dişi",
            "Yaş: 3 yaşındaDoğum tarihi: 12 Temmuz 2023Mikroçip No: 985000000000001Tarbil Durumu: KayıtlıKayıt tarihi: 01 Mayıs 2025"],
        ["Ayşe Yılmaz\r\nTel: 0544 777 88 99", "PAMUK\r\nKöpek / Melez  Erkek",
            "Yaş: 1 yaşındaDoğum tarihi: 05 Şubat 2025Mikroçip No: 985000000000002Tarbil Durumu: KayıtlıKayıt tarihi: 06 Şubat 2025"],
        // Listelerde olmayan hasta: eşleşmez.
        ["Bilinmeyen Kişi\r\nTel: 0555 000 00 00", "BONCUK\r\nKedi / Tekir  Dişi", "Yaş: 2 yaşındaDoğum tarihi: 01 Ocak 2024"],
    ]);

    private static byte[] Sheet(string title, object?[] header, object?[][] rows)
        => ImportTestFiles.Xlsx(("Sheet1", [[title], header, .. rows]));

    private static ImportUploadReader Reader() => new(Options.Create(ImportTestFiles.Limits()), new FakeTimeProvider(Now));

    private static ImportFileSet Files(params byte[][] files)
        => new(null, null, null, null, KolayvetFiles: files.Select((f, i) => ImportTestFiles.XlsxFile(f, $"kolayvet-{i}.xlsx")).ToList());

    private static ImportReadResult Read(ImportFileSet files)
    {
        var reader = Reader();
        var prepared = reader.Prepare(files, null, null, ImportSourceProfile.Kolayvet);
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
    public void Files_Should_Be_Recognized_By_Header_In_Any_Order_And_Converted_In_Validation_Order()
    {
        var result = Read(Files(ReportBytes, VaccinesBytes, PetsBytes, CustomersBytes));

        result.Tables.Select(t => t.Table).Should().Equal(
            ImportTable.Customers, ImportTable.Pets, ImportTable.Vaccinations, ImportTable.Appointments);
        Rows(result, ImportTable.Customers).Should().HaveCount(3, "sayfa başlığı satırı veri sayılmaz");
    }

    [Fact]
    public void Unrecognized_File_Should_Be_Rejected_With_Its_Name()
    {
        var other = ImportTestFiles.Xlsx(("Sheet1", [["Ürün", "Barkod"], ["Kuduz", "123"]]));
        var reader = Reader();
        var prepared = reader.Prepare(Files(CustomersBytes, other), null, null, ImportSourceProfile.Kolayvet).Value!;

        var read = reader.Read(prepared, trialTenant: false);

        read.Error.Code.Should().Be("Imports.UnsupportedFile");
        read.Error.Message.Should().Contain("kolayvet-1.xlsx").And.Contain("tanınmadı");
    }

    [Fact]
    public void Same_File_Twice_Should_Be_Rejected()
    {
        var reader = Reader();
        var prepared = reader.Prepare(Files(PetsBytes, PetsBytes), null, null, ImportSourceProfile.Kolayvet).Value!;

        reader.Read(prepared, trialTenant: false).Error.Message.Should().Contain("Hasta Listesi dosyası birden fazla");
    }

    [Fact]
    public void Patient_Report_Without_Pet_List_Should_Be_Rejected()
    {
        var reader = Reader();
        var prepared = reader.Prepare(Files(CustomersBytes, ReportBytes), null, null, ImportSourceProfile.Kolayvet).Value!;

        reader.Read(prepared, trialTenant: false).Error.Message.Should().Contain("Hasta Listesi dosyasıyla birlikte");
    }

    [Fact]
    public void Kolayvet_Files_Should_Require_Kolayvet_Profile_And_Stand_Alone()
    {
        var reader = Reader();

        reader.Prepare(Files(CustomersBytes), null, null, ImportSourceProfile.Generic).Error.Message.Should().Contain("Kolayvet profiliyle");
        reader.Prepare(Files(CustomersBytes) with { CustomersCsv = ImportTestFiles.Csv("AdSoyad\nA B\n", "m.csv") }, null, null, ImportSourceProfile.Kolayvet)
            .Error.Message.Should().Contain("birlikte gönderilemez");
        reader.Prepare(Files(CustomersBytes, PetsBytes, VaccinesBytes, ReportBytes, CustomersBytes), null, null, ImportSourceProfile.Kolayvet)
            .Error.Message.Should().Contain("En çok 4");
    }

    [Fact]
    public void Content_Hash_Should_Not_Depend_On_Upload_Order()
    {
        var reader = Reader();

        var first = reader.Prepare(Files(CustomersBytes, PetsBytes), null, null, ImportSourceProfile.Kolayvet).Value!.ContentHash;
        var second = reader.Prepare(Files(PetsBytes, CustomersBytes), null, null, ImportSourceProfile.Kolayvet).Value!.ContentHash;
        var other = reader.Prepare(Files(CustomersBytes), null, null, ImportSourceProfile.Kolayvet).Value!.ContentHash;

        second.Should().Be(first);
        other.Should().NotBe(first);
    }

    // ================================================================ Müşteri

    [Fact]
    public void Customers_Should_Keep_First_Mobile_Number_Balance_And_Identity()
    {
        var rows = Rows(Read(Files(CustomersBytes)), ImportTable.Customers);

        rows[0].Should().Contain(new Dictionary<string, string?>
        {
            ["MusteriNo"] = "1001",
            ["AdSoyad"] = "Ayşe Yılmaz",
            ["Telefon"] = "0532 111 22 33",
            ["AcikBakiye"] = null,
            ["KimlikNo"] = "10000000146",
        });
        rows[1]["AcikBakiye"].Should().Be("150.50");
        rows[2]["AcikBakiye"].Should().Be("-20.00", "eksi bakiye devir alacağıdır (ADR-048 T3)");
    }

    // ================================================================ Hasta ve detaylı rapor

    [Fact]
    public void Pets_Should_Link_Owner_By_Number_And_Take_Birth_Date_And_Chip_From_Report()
    {
        var result = Read(Files(CustomersBytes, PetsBytes, ReportBytes));
        var pets = Rows(result, ImportTable.Pets);

        pets[0].Should().Contain(new Dictionary<string, string?>
        {
            ["HayvanNo"] = "2001",
            ["MusteriNo"] = "1001",
            ["HayvanAdi"] = "Pamuk",
            ["Tur"] = "Kedi",
            ["Irk"] = "Tekir",
            ["Cinsiyet"] = "Dişi",
            ["DogumTarihi"] = "2023-07-12",
            ["Mikrocip"] = "985000000000001",
            ["Not"] = "Eski programa kayıt: 01.05.2025\nTarbil (eski program): Kayıtlı",
        });
        pets[2]["DogumTarihi"].Should().Be("2025-02-05", "aynı adlı ikinci müşteri telefonla ayırt edilir");
        pets[2]["Mikrocip"].Should().Be("985000000000002");
        pets[3]["Mikrocip"].Should().Be("985000000000099", "hasta listesinde dolu mikroçip korunur");
        pets[1]["DogumTarihi"].Should().BeNull("raporda yok");

        var note = result.Warnings.Single(w => w.Code == KolayvetNotes.PatientReportCode);
        note.Severity.Should().Be(ImportNoteSeverity.Attention);
        note.Parts!.Select(p => p.Count).Should().Equal(1, 2);
    }

    [Fact]
    public void Missing_Patient_Report_Should_Warn_That_Birth_Dates_Will_Not_Come()
    {
        var result = Read(Files(CustomersBytes, PetsBytes));

        result.Warnings.Should().ContainSingle(w => w.Code == KolayvetNotes.PatientReportMissingCode).Which.Count.Should().Be(4);
        Rows(result, ImportTable.Pets).Should().OnlyContain(p => p["DogumTarihi"] == null);
    }

    // ================================================================ Aşı

    [Fact]
    public void Applied_Vaccine_Should_Use_Applied_Date_And_Take_Next_Date_From_Same_Series()
    {
        var vaccines = Rows(Read(Files(PetsBytes, VaccinesBytes)), ImportTable.Vaccinations);

        vaccines.Should().HaveCount(2);
        vaccines[0].Should().Contain(new Dictionary<string, string?>
        {
            ["HayvanNo"] = "2001",
            ["AsiAdi"] = "Karma 1 Aşısı",
            ["UygulamaTarihi"] = "2026-10-03",
            ["SonrakiAsiTarihi"] = "2026-10-22",
            ["Not"] = null,
        });
        vaccines[1].Should().Contain(new Dictionary<string, string?>
        {
            ["UygulamaTarihi"] = "2025-09-09",
            ["Seri"] = "L-77",
            ["Not"] = "Kolayvet'te yapılma tarihi yok; planlanan tarih yazıldı",
        });
    }

    [Fact]
    public void Future_Planned_Vaccine_Without_Prior_Dose_Should_Become_Ten_O_Clock_Appointment()
    {
        var result = Read(Files(PetsBytes, VaccinesBytes));

        Rows(result, ImportTable.Appointments).Should().ContainSingle().Which.Should().Contain(new Dictionary<string, string?>
        {
            ["HayvanNo"] = "2001",
            ["Tarih"] = "2026-12-15",
            ["Saat"] = "10:00",
            ["RandevuTuru"] = "Aşı",
            ["Not"] = "Aşı: Kuduz; Kolayvet aşı takviminden, saat belirtilmemiş",
        });

        var skipped = result.Warnings.Single(w => w.Code == KolayvetNotes.VaccinesSkippedCode);
        skipped.Parts!.Should().ContainSingle(p => p.Label == "Hiç uygulanmamış ve tarihi geçmiş aşı" && p.Count == 1);
        result.Warnings.Single(w => w.Code == KolayvetNotes.VaccineConversionCode).Parts!.Select(p => p.Count).Should().Equal(2, 1);
    }

    // ================================================================ Eksik dosya uyarısı

    [Fact]
    public void Page_Sized_File_And_Unknown_Links_Should_Warn_About_Truncated_Download()
    {
        var twenty = Sheet("Müşteri Listesi | Kolayvet", CustomerHeader,
            Enumerable.Range(1, 20).Select(i => new object?[] { 5000 + i, $"Kişi {i}", "05321112233", 0d, null }).ToArray());

        var result = Read(Files(twenty, PetsBytes));

        result.Warnings.Should().Contain(w => w.Code == KolayvetNotes.PossiblyTruncatedCode && w.Table == ImportTable.Customers && w.Count == 20);
        result.Warnings.Should().Contain(w => w.Code == KolayvetNotes.MissingLinksCode && w.Table == ImportTable.Pets && w.Count == 4);
    }

    [Fact]
    public void Complete_Files_Should_Not_Warn_About_Truncation()
        => Read(Files(CustomersBytes, PetsBytes, VaccinesBytes)).Warnings
            .Should().NotContain(w => w.Code == KolayvetNotes.PossiblyTruncatedCode || w.Code == KolayvetNotes.MissingLinksCode);

    // ================================================================ Hücreler

    [Theory]
    [InlineData("28 Kasım 2026", "2026-11-28")]
    [InlineData("01 Şubat 2025", "2025-02-01")]
    [InlineData("9 AĞUSTOS 2024", "2024-08-09")]
    [InlineData("2026-10-03", "2026-10-03")]
    [InlineData("31 Şubat 2025", null)]
    [InlineData("12 Foo 2025", null)]
    [InlineData("null", null)]
    public void ParseDate_Should_Read_Turkish_Month_Names(string value, string? expected)
        => KolayvetCells.IsoDate(KolayvetCells.ParseDate(value)).Should().Be(expected);

    [Theory]
    [InlineData("0532 111 22 330532 111 22 33 Hemen Ara", "0532 111 22 33")]
    [InlineData("+90 532 111 22 33", "+90 532 111 22 33")]
    [InlineData("0212 000 00 00 Ara", "0212 000 00 00")]
    [InlineData("  ", null)]
    public void Phone_Should_Take_First_Mobile_Number(string value, string? expected)
        => KolayvetCells.Phone(value).Should().Be(expected);

    [Fact]
    public void Report_Cells_Should_Be_Split_By_Labels()
    {
        KolayvetCells.CustomerInfo("Ali Veli ( Pamuk ) Tel: 0532 111 22 33T.C.: 10000000146").Should().Be(("Ali Veli ( Pamuk )", "0532 111 22 33"));
        KolayvetCells.PetInfo("SARI KIZ\r\nKöpek / Golden Retriever  Dişi").Should().Be(("SARI KIZ", "Köpek"));
        KolayvetCells.PetInfo("BONCUK\r\nGine Domuzu / Abyssinian  Dişi").Should().Be(("BONCUK", "Gine Domuzu"));
        KolayvetCells.PetInfo("TARÇIN Kedi / Tekir  Erkek").Should().Be(("TARÇIN", "Kedi"));
        KolayvetCells.PetDetails("Yaş: 2 yaşındaDoğum tarihi: 03 Mart 2024Mikroçip No: 985000000000003Kayıt tarihi: 04 Mart 2024")
            .Should().Be((new DateOnly(2024, 3, 3), "985000000000003", (string?)null, new DateOnly(2024, 3, 4)));
    }
}
