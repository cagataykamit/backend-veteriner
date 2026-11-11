using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Template;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>Tam geçiş (ADR-048) okuma kuralları: tutar/saat ayrıştırma, sayfa adları, zorunlu alanlar, şablon ve ek izinler.</summary>
public sealed class ImportFullMigrationSchemaTests
{
    [Theory]
    [InlineData("1250", 1250)]
    [InlineData("1250,5", 1250.5)]
    [InlineData("1250.50", 1250.50)]
    [InlineData("1.250,50", 1250.50)]
    [InlineData("1,250.50", 1250.50)]
    [InlineData("1.250", 1250)]
    [InlineData("12,345", 12345)]
    [InlineData("12.345.678,90", 9_999_999.99)]
    [InlineData("₺1.250,50", 1250.50)]
    [InlineData("1250 TL", 1250)]
    [InlineData("-300", -300)]
    public void ParseAmount_Should_Read_Turkish_And_Invariant_Formats(string text, double expected)
    {
        var (value, valid) = ImportValueParsers.ParseAmount(text);

        if (expected > 9_999_999)
        {
            valid.Should().BeFalse("tavan aşılıyor");
            return;
        }

        valid.Should().BeTrue();
        value.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("100 USD")]
    [InlineData("€50")]
    [InlineData("on bin")]
    [InlineData("1.2.3")]
    public void ParseAmount_Should_Reject_Foreign_Currency_And_Garbage(string text)
    {
        ImportValueParsers.ParseAmount(text).Should().Be(((decimal?)null, false));
    }

    [Fact]
    public void ParseAmount_Empty_Should_Be_Valid_Null()
        => ImportValueParsers.ParseAmount("  ").Should().Be(((decimal?)null, true));

    [Theory]
    [InlineData("14:30", 14, 30)]
    [InlineData("9.05", 9, 5)]
    [InlineData("1899-12-30T14:30:00", 14, 30)]
    [InlineData("0,5", 12, 0)]
    public void ParseTimeOfDay_Should_Read_Common_Forms(string text, int hour, int minute)
        => ImportValueParsers.ParseTimeOfDay(text).Should().Be(new TimeOnly(hour, minute));

    [Theory]
    [InlineData("25:00")]
    [InlineData("öğlen")]
    public void ParseTimeOfDay_Should_Reject_Invalid(string text)
        => ImportValueParsers.ParseTimeOfDay(text).Should().BeNull();

    [Fact]
    public void TimeOfDayIn_Should_Read_Time_From_Date_Cell()
    {
        ImportValueParsers.TimeOfDayIn("20.12.2026 14:30").Should().Be(new TimeOnly(14, 30));
        ImportValueParsers.TimeOfDayIn("20.12.2026").Should().BeNull();
    }

    [Theory]
    [InlineData("38,6", 38.6)]
    [InlineData("39 °C", 39)]
    public void ParseTemperature_Should_Read_Celsius(string text, double expected)
        => ImportValueParsers.ParseTemperature(text).Should().Be(((decimal?)(decimal)expected, true));

    [Theory]
    [InlineData("KlinikGecmis", ImportTable.ClinicalRecords)]
    [InlineData("Muayeneler", ImportTable.ClinicalRecords)]
    [InlineData("Hesap Hareketleri", ImportTable.AccountEntries)]
    [InlineData("Randevular", ImportTable.Appointments)]
    [InlineData("Ürünler", ImportTable.Products)]
    public void New_Sheet_Names_Should_Be_Recognized(string sheet, ImportTable expected)
    {
        ImportCanonicalSchema.TryResolveSheet(sheet, out var table).Should().BeTrue();
        table.Should().Be(expected);
    }

    [Fact]
    public void Table_Keys_Should_Round_Trip_For_All_Tables()
    {
        foreach (var table in ImportTables.All)
        {
            ImportCanonicalNames.TryParseKey(ImportCanonicalNames.Key(table), out var parsed).Should().BeTrue();
            parsed.Should().Be(table);
        }
    }

    [Fact]
    public void Missing_Required_Should_Describe_New_Tables()
    {
        ImportCanonicalSchema.MissingRequired(ImportTable.AccountEntries, ["MusteriNo", "Tarih"])
            .Should().Equal("IslemTuru", "Tutar");
        ImportCanonicalSchema.MissingRequired(ImportTable.ClinicalRecords, ["Tarih"])
            .Should().ContainSingle().Which.Should().Contain("HastaNo");
        ImportCanonicalSchema.MissingRequired(ImportTable.Products, ["Ad"]).Should().BeEmpty();
        ImportCanonicalSchema.MissingRequired(ImportTable.Appointments, ["HayvanNo", "Tarih"]).Should().BeEmpty();
    }

    [Fact]
    public void Suggestions_Should_Recognize_Common_Headers_Of_New_Tables()
    {
        var suggested = ImportCanonicalSchema.Suggest(ImportTable.ClinicalRecords, ["Hasta No", "Muayene Tarihi", "Teşhis", "Reçete", "Ateş"])
            .Select(s => s.Suggested);
        suggested.Should().Equal("HayvanNo", "Tarih", "Tani", "Recete", "Ates");

        ImportCanonicalSchema.Suggest(ImportTable.Products, ["Ürün Adı", "Satış Fiyatı", "Barkod"])
            .Select(s => s.Suggested).Should().Equal("Ad", "SatisFiyati", "Barkod");
    }

    [Fact]
    public void Csv_Template_File_Names_Should_Match_Contract()
    {
        ImportTemplateBuilder.CsvFileName(ImportTable.ClinicalRecords).Should().Be("vetinity-iceaktar-klinik-gecmis.csv");
        ImportTemplateBuilder.CsvFileName(ImportTable.AccountEntries).Should().Be("vetinity-iceaktar-hesap-hareketleri.csv");
        ImportTemplateBuilder.CsvFileName(ImportTable.Appointments).Should().Be("vetinity-iceaktar-randevular.csv");
        ImportTemplateBuilder.CsvFileName(ImportTable.Products).Should().Be("vetinity-iceaktar-urunler.csv");
    }

    [Fact]
    public void Write_Permissions_Should_Cover_New_Tables_And_Opening_Balance()
    {
        var batch = ImportBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), DateTime.UtcNow).Value!;
        batch.AttachFile(new string('a', 64), new Dictionary<ImportTable, int>
        {
            [ImportTable.Customers] = 1,
            [ImportTable.ClinicalRecords] = 1,
            [ImportTable.Appointments] = 1,
            [ImportTable.Products] = 1,
        }).IsSuccess.Should().BeTrue();
        batch.ApplyMapping(
            """{"columns":{"customers":{"AdSoyad":"Ad","AcikBakiye":"Bakiye"}}}""", null, DateTime.UtcNow).IsSuccess.Should().BeTrue();

        ImportWritePermissions.Required(batch).Should().BeEquivalentTo(
        [
            PermissionCatalog.Clients.Create, PermissionCatalog.Examinations.Create, PermissionCatalog.Appointments.Create,
            PermissionCatalog.Products.Create, PermissionCatalog.Charges.Create,
        ]);
    }
}
