using System.Text;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Application.Imports.Template;
using Backend.Veteriner.Domain.Imports;
using ClosedXML.Excel;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

public sealed class ImportTemplateBuilderTests
{
    [Fact]
    public void Xlsx_Should_Have_Three_Data_Sheets_With_Only_Canonical_Headers_And_An_Explanation_Sheet()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ImportTemplateBuilder.BuildXlsx()));

        workbook.Worksheets.Select(w => w.Name).Should().Equal("Musteriler", "Hastalar", "Asilar", "Aciklama");
        foreach (var table in ImportCanonicalNames.Tables)
        {
            var sheet = workbook.Worksheet(ImportCanonicalSchema.SheetName(table));
            var expected = ImportCanonicalSchema.Fields(table).Select(f => f.Header).ToList();

            sheet.Row(1).Cells(1, expected.Count).Select(c => c.GetString()).Should().Equal(expected);
            // Veri sayfalarında örnek satır yok: doldurulmadan yüklenen şablon sahte kayıt oluşturmaz.
            sheet.LastRowUsed()!.RowNumber().Should().Be(1);
        }
    }

    [Fact]
    public void Explanation_Sheet_Should_Describe_Every_Field_With_An_Example()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ImportTemplateBuilder.BuildXlsx()));
        var sheet = workbook.Worksheet(ImportTemplateBuilder.ExplanationSheetName);

        var fieldCount = ImportCanonicalNames.Tables.Sum(t => ImportCanonicalSchema.Fields(t).Count);
        sheet.LastRowUsed()!.RowNumber().Should().Be(fieldCount + 1);
        sheet.Row(1).Cells(1, 5).Select(c => c.GetString())
            .Should().Equal("Tablo (sayfa)", "Başlık", "Zorunlu", "Açıklama", "Örnek");

        var phone = Enumerable.Range(2, fieldCount)
            .Select(r => sheet.Row(r))
            .First(r => r.Cell(1).GetString() == "Musteriler" && r.Cell(2).GetString() == "Telefon");
        phone.Cell(5).GetString().Should().Be("05321234567");
    }

    [Fact]
    public void Text_Sensitive_Columns_Should_Be_Text_Formatted()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ImportTemplateBuilder.BuildXlsx()));
        var pets = workbook.Worksheet("Hastalar");
        var fields = ImportCanonicalSchema.Fields(ImportTable.Pets).Select(f => f.Name).ToList();

        string Format(string field) => pets.Column(fields.IndexOf(field) + 1).Style.NumberFormat.Format;

        Format("Mikrocip").Should().Be("@");
        Format("DogumTarihi").Should().Be("@");
        Format("SahipTelefon").Should().Be("@");
        Format("HayvanAdi").Should().NotBe("@");
    }

    [Fact]
    public void Template_Data_Sheets_Should_Be_Recognized_By_The_Reader_As_Header_Only()
    {
        var result = XlsxTableReader.Read(ImportTemplateBuilder.BuildXlsx(), ImportTestFiles.Limits());

        // Yalnız başlık: veri yok → EmptyFile (tanınan sayfa var, satır yok).
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Imports.EmptyFile");
    }

    [Fact]
    public void Filled_Template_Should_Round_Trip_Through_The_Reader_With_Exact_Suggestions()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ImportTemplateBuilder.BuildXlsx()));
        workbook.Worksheet("Musteriler").Cell(2, 2).Value = "Ayşe Yılmaz";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var read = XlsxTableReader.Read(stream.ToArray(), ImportTestFiles.Limits()).Value!;

        var customers = read.Tables.Single();
        customers.Headers.Should().Equal(ImportCanonicalSchema.Fields(ImportTable.Customers).Select(f => f.Name));
        ImportCanonicalSchema.Suggest(ImportTable.Customers, customers.Headers)
            .Should().OnlyContain(s => s.Confidence == ImportSuggestionConfidence.Exact);
    }

    [Fact]
    public void Pet_Columns_Should_Say_Hasta_While_Api_Keys_Stay_The_Same()
    {
        // ADR-022: kullanıcıya görünen başlık "hasta"; API anahtarı (eşleme, sorun alanı) değişmez.
        using var workbook = new XLWorkbook(new MemoryStream(ImportTemplateBuilder.BuildXlsx()));
        var petHeaders = workbook.Worksheet("Hastalar").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        var vaccinationHeaders = workbook.Worksheet("Asilar").Row(1).CellsUsed().Select(c => c.GetString()).ToList();

        petHeaders.Should().Contain(["HastaNo", "HastaAdi", "Tur"]).And.NotContain(["HayvanNo", "HayvanAdi"]);
        vaccinationHeaders.Should().Contain(["HastaNo", "HastaAdi", "Tur"]).And.NotContain(["HayvanNo", "HayvanAdi"]);

        var explanation = workbook.Worksheet(ImportTemplateBuilder.ExplanationSheetName);
        var vaccinationPetNo = explanation.RowsUsed()
            .First(r => r.Cell(1).GetString() == "Asilar" && r.Cell(2).GetString() == "HastaNo");
        vaccinationPetNo.Cell(3).GetString().Should().Contain("HastaNo").And.Contain("HastaAdi").And.NotContain("Hayvan");
        vaccinationPetNo.Cell(4).GetString().Should().Be("Hasta sayfasındaki HastaNo ile aynı değer.");

        ImportCanonicalSchema.Fields(ImportTable.Pets).Select(f => f.Name).Should().Contain(["HayvanNo", "HayvanAdi"]);
        ImportCanonicalSchema.Suggest(ImportTable.Pets, ["HastaNo", "HastaAdi"])
            .Select(s => (s.Suggested, s.Confidence)).Should().Equal(
                ("HayvanNo", ImportSuggestionConfidence.Exact),
                ("HayvanAdi", ImportSuggestionConfidence.Exact));
    }

    [Fact]
    public void Filled_Template_Pet_And_Vaccination_Sheets_Should_Map_Exactly_To_Api_Keys()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ImportTemplateBuilder.BuildXlsx()));
        workbook.Worksheet("Hastalar").Cell(2, 1).Value = "M-1";
        workbook.Worksheet("Asilar").Cell(2, 1).Value = "H-1";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var read = XlsxTableReader.Read(stream.ToArray(), ImportTestFiles.Limits()).Value!;

        foreach (var table in new[] { ImportTable.Pets, ImportTable.Vaccinations })
        {
            var headers = read.Tables.Single(t => t.Table == table).Headers;
            var suggestions = ImportCanonicalSchema.Suggest(table, headers);
            suggestions.Should().OnlyContain(s => s.Confidence == ImportSuggestionConfidence.Exact);
            suggestions.Select(s => s.Suggested).Should().Equal(ImportCanonicalSchema.Fields(table).Select(f => f.Name));
        }
    }

    [Theory]
    [InlineData(ImportTable.Customers, "vetinity-iceaktar-musteriler.csv")]
    [InlineData(ImportTable.Pets, "vetinity-iceaktar-hastalar.csv")]
    [InlineData(ImportTable.Vaccinations, "vetinity-iceaktar-asilar.csv")]
    public void Csv_Should_Be_Utf8_Bom_Semicolon_Header_Only(ImportTable table, string fileName)
    {
        var bytes = ImportTemplateBuilder.BuildCsv(table);

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.Should().Be(string.Join(';', ImportCanonicalSchema.Fields(table).Select(f => f.Header)) + "\r\n");
        ImportTemplateBuilder.CsvFileName(table).Should().Be(fileName);

        var parsed = CsvTableReader.Read(table, bytes, null, null, ImportTestFiles.Limits(), []).Value;
        parsed!.Headers.Should().Equal(ImportCanonicalSchema.Fields(table).Select(f => f.Header));
        parsed.Rows.Should().BeEmpty();
    }
}
