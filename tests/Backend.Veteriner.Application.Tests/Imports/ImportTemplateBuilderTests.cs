using System.Text;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Application.Imports.Template;
using Backend.Veteriner.Domain.Imports;
using ClosedXML.Excel;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

public sealed class ImportTemplateBuilderTests
{
    [Fact]
    public void Xlsx_Should_Have_Three_Data_Sheets_With_Only_Canonical_Headers_And_An_Explanation_Sheet()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ImportTemplateBuilder.BuildXlsx()));

        workbook.Worksheets.Select(w => w.Name).Should().Equal("Musteriler", "Hayvanlar", "Asilar", "Aciklama");
        foreach (var table in ImportCanonicalNames.Tables)
        {
            var sheet = workbook.Worksheet(ImportCanonicalSchema.SheetName(table));
            var expected = ImportCanonicalSchema.Fields(table).Select(f => f.Name).ToList();

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
        var pets = workbook.Worksheet("Hayvanlar");
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

    [Theory]
    [InlineData(ImportTable.Customers, "vetinity-iceaktar-musteriler.csv")]
    [InlineData(ImportTable.Pets, "vetinity-iceaktar-hayvanlar.csv")]
    [InlineData(ImportTable.Vaccinations, "vetinity-iceaktar-asilar.csv")]
    public void Csv_Should_Be_Utf8_Bom_Semicolon_Header_Only(ImportTable table, string fileName)
    {
        var bytes = ImportTemplateBuilder.BuildCsv(table);

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.Should().Be(string.Join(';', ImportCanonicalSchema.Fields(table).Select(f => f.Name)) + "\r\n");
        ImportTemplateBuilder.CsvFileName(table).Should().Be(fileName);

        var parsed = CsvTableReader.Read(table, bytes, null, null, ImportTestFiles.Limits(), []).Value;
        parsed!.Headers.Should().Equal(ImportCanonicalSchema.Fields(table).Select(f => f.Name));
        parsed.Rows.Should().BeEmpty();
    }
}
