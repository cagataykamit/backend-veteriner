using System.Text;
using Backend.Veteriner.Application.Imports.Reporting;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using ClosedXML.Excel;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>Hata raporu dosyası: orijinal satır + hata kodu/açıklama; formül enjeksiyonu güvenliği (ImportCellSafety).</summary>
public sealed class ImportErrorReportWriterTests
{
    private static ImportErrorReportRow Row(
        ImportTable table,
        int number,
        IReadOnlyDictionary<string, string?> source,
        params ImportIssue[] errors)
        => new(table, number, source, errors);

    private static Dictionary<string, string?> Source(params (string Key, string? Value)[] cells)
        => cells.ToDictionary(c => c.Key, c => c.Value);

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    [Fact]
    public void Xlsx_Should_Have_One_Sheet_Per_Table_With_Original_Columns_And_Error_Columns()
    {
        var bytes = ImportErrorReportWriter.BuildXlsx(
        [
            Row(ImportTable.Customers, 5, Source(("Ad Soyad", null), ("Cep", "0532")),
                ImportRules.RequiredMissing.At("AdSoyad")),
            Row(ImportTable.Pets, 9, Source(("Hayvan", "Pamuk"), ("Tür", "Ejderha")),
                ImportRules.SpeciesUnmapped.At("Tur", value: "Ejderha"),
                ImportRules.OwnerNotFound.At("SahipAdSoyad")),
        ]);

        using var wb = Open(bytes);
        wb.Worksheets.Select(s => s.Name).Should().Equal("Musteriler", "Hayvanlar");

        var customers = wb.Worksheet("Musteriler");
        customers.Row(1).Cells(1, 6).Select(c => c.GetString()).Should()
            .Equal("Ad Soyad", "Cep", "SatirNo", "Durum", "HataKodu", "Aciklama");
        customers.Cell(2, 2).GetString().Should().Be("0532");
        customers.Cell(2, 3).GetValue<int>().Should().Be(5);
        customers.Cell(2, 4).GetString().Should().Be("Hata");
        customers.Cell(2, 5).GetString().Should().Be("Imports.RequiredMissing");

        var pets = wb.Worksheet("Hayvanlar");
        pets.Cell(2, 5).GetString().Should().Be("Imports.SpeciesUnmapped; Imports.OwnerNotFound");
        pets.Cell(2, 6).GetString().Should().Contain("; ").And.Contain("katalogda bulunamadı");
    }

    [Fact]
    public void Xlsx_Without_Errors_Should_Have_Single_Empty_Sheet()
    {
        using var wb = Open(ImportErrorReportWriter.BuildXlsx([]));

        wb.Worksheets.Select(s => s.Name).Should().Equal(ImportErrorReportWriter.NoErrorsSheetName);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\",\"t\")")]
    [InlineData("+90 532 123 45 67")]
    [InlineData("-1+1")]
    [InlineData("@SUM(1+1)")]
    public void Xlsx_Should_Store_Formula_Like_Cells_And_Headers_As_Quote_Prefixed_Text(string malicious)
    {
        var bytes = ImportErrorReportWriter.BuildXlsx(
            [Row(ImportTable.Customers, 2, Source((malicious, malicious)), ImportRules.RequiredMissing.At("AdSoyad"))]);

        // ClosedXML başa eklenen ' işaretini Excel'in metin öneki (quotePrefix) olarak yazar: hücre metindir, formül değildir.
        using var wb = Open(bytes);
        var sheet = wb.Worksheet("Musteriler");
        foreach (var cell in new[] { sheet.Cell(1, 1), sheet.Cell(2, 1) })
        {
            cell.HasFormula.Should().BeFalse();
            cell.DataType.Should().Be(XLDataType.Text);
            cell.GetString().Should().Be(malicious);
            cell.Style.IncludeQuotePrefix.Should().BeTrue();
        }
    }

    [Fact]
    public void Xlsx_Should_Not_Prefix_Harmless_Text()
    {
        var bytes = ImportErrorReportWriter.BuildXlsx(
            [Row(ImportTable.Customers, 2, Source(("Ad", "Ayşe Yılmaz")), ImportRules.RequiredMissing.At("AdSoyad"))]);

        using var wb = Open(bytes);
        wb.Worksheet("Musteriler").Cell(2, 1).GetString().Should().Be("Ayşe Yılmaz");
    }

    [Fact]
    public void Csv_Should_Have_Bom_Fixed_Columns_And_Quote_Special_Characters()
    {
        var bytes = ImportErrorReportWriter.BuildCsv(
        [
            Row(ImportTable.Pets, 9, Source(("Hayvan", "Pa;muk"), ("Not", "satır\nsonu \"x\"")),
                ImportRules.SpeciesUnmapped.At("Tur")),
            Row(ImportTable.Customers, 3, Source(("Ad", null)), ImportRules.RequiredMissing.At("AdSoyad")),
        ]);

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Tablo;SatirNo;Durum;HataKodu;Aciklama;Kaynak");
        // Müşteri tablosu önce (tablo sırası), sonra hasta.
        lines[1].Should().StartWith("Müşteri;3;Hata;Imports.RequiredMissing;");
        text.Should().Contain("\"Hayvan=Pa;muk|Not=satır\nsonu \"\"x\"\"\"");
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("@x")]
    [InlineData("-5")]
    [InlineData("+9")]
    public void Csv_Should_Prefix_Formula_Like_Source_Cells(string malicious)
    {
        var bytes = ImportErrorReportWriter.BuildCsv(
            [Row(ImportTable.Customers, 2, Source((malicious, "x")), ImportRules.RequiredMissing.At("AdSoyad"))]);

        var text = Encoding.UTF8.GetString(bytes);
        // Kaynak sütunu başlık=değer ile başlar; başlık formül karakteriyle başlıyorsa hücre başına ' eklenir.
        text.Should().Contain($";'{malicious}=x");
    }
}
