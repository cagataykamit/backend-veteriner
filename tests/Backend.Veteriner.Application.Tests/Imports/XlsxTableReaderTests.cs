using System.Text;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Domain.Imports;
using ClosedXML.Excel;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

public sealed class XlsxTableReaderTests
{
    private static readonly object?[][] CustomerRows =
    [
        ["Ad Soyad", "Telefon", "Eposta"],
        ["Ayşe Yılmaz", "05321234567", "ayse@example.com"],
        ["Veli Kaya", null, null],
    ];

    private static ImportFileLimits Limits(Action<ImportFileLimits>? configure = null) => ImportTestFiles.Limits(configure);

    private static byte[] CustomersOnly() => ImportTestFiles.Xlsx(("Musteriler", CustomerRows));

    [Fact]
    public void Recognized_Sheets_Should_Be_Read_And_Unknown_Sheets_Ignored()
    {
        var bytes = ImportTestFiles.Xlsx(
            ("Musteriler", CustomerRows),
            ("Hayvanlar", [["HayvanAdi", "Tur"], ["Pamuk", "Kedi"]]),
            ("Aciklama", [["Not"], ["Bu sayfa okunmaz"]]));

        var result = XlsxTableReader.Read(bytes, Limits());

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        result.Value!.Tables.Select(t => t.Table).Should().Equal(ImportTable.Customers, ImportTable.Pets);
        var customers = result.Value.Tables[0];
        customers.Headers.Should().Equal("Ad Soyad", "Telefon", "Eposta");
        customers.Rows.Should().HaveCount(2);
        customers.Rows[0].RowNumber.Should().Be(2);
        customers.Rows[0].Cells.Should().Equal("Ayşe Yılmaz", "05321234567", "ayse@example.com");
        customers.Rows[1].Cells.Should().Equal("Veli Kaya", null, null);
    }

    [Theory]
    [InlineData("MÜŞTERİLER")]
    [InlineData("musteri")]
    [InlineData("Customers")]
    public void Sheet_Names_Should_Match_Case_And_Accent_Insensitively(string sheetName)
    {
        var bytes = ImportTestFiles.Xlsx((sheetName, CustomerRows));

        var result = XlsxTableReader.Read(bytes, Limits());

        result.Value!.Tables.Should().ContainSingle().Which.Table.Should().Be(ImportTable.Customers);
    }

    [Fact]
    public void Cell_Values_Should_Be_Normalized_To_Text()
    {
        var bytes = ImportTestFiles.Xlsx(("Hayvanlar",
        [
            ["HayvanAdi", "DogumTarihi", "Mikrocip", "Telefon", "Agirlik", "Kisir"],
            ["Pamuk", new DateTime(2021, 3, 15), 985112345678901L, 5321234567L, 4.5, true],
            ["Boncuk", new DateTime(2020, 1, 2, 10, 30, 0), "985112345678902", "0532 123 45 67", 12.25, false],
        ]));

        var rows = XlsxTableReader.Read(bytes, Limits()).Value!.Tables.Single().Rows;

        rows[0].Cells.Should().Equal("Pamuk", "2021-03-15", "985112345678901", "5321234567", "4.5", "true");
        rows[1].Cells.Should().Equal("Boncuk", "2020-01-02T10:30:00", "985112345678902", "0532 123 45 67", "12.25", "false");
    }

    [Fact]
    public void Formula_Like_Text_Cells_Should_Be_Kept_As_Plain_Text()
    {
        var bytes = ImportTestFiles.Xlsx(("Musteriler",
        [
            ["AdSoyad", "Telefon", "Adres", "Not"],
            ["=1+1", "+90 532 123 45 67", "-5", "@SUM(A1:A2)"],
        ]));

        var row = XlsxTableReader.Read(bytes, Limits()).Value!.Tables.Single().Rows.Single();

        row.Cells.Should().Equal("=1+1", "+90 532 123 45 67", "-5", "@SUM(A1:A2)");
    }

    [Fact]
    public void Formula_Cells_Should_Not_Be_Evaluated()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Musteriler");
        sheet.Cell(1, 1).Value = "AdSoyad";
        sheet.Cell(1, 2).Value = "Telefon";
        sheet.Cell(2, 1).Value = "Ali";
        sheet.Cell(2, 2).FormulaA1 = "1+1";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var row = XlsxTableReader.Read(stream.ToArray(), Limits()).Value!.Tables.Single().Rows.Single();

        // Önbellek değeri yok: formül çalıştırılmadı, hücre boş okundu (2 değil).
        row.Cells[0].Should().Be("Ali");
        row.Cells[1].Should().BeNull();
    }

    [Fact]
    public void Blank_Rows_And_Columns_Should_Be_Ignored_And_Excel_Row_Numbers_Kept()
    {
        var bytes = ImportTestFiles.Xlsx(("Musteriler",
        [
            ["AdSoyad", null, "Telefon"],
            ["Ali", null, "1"],
            [null, null, null],
            ["Veli", null, "2"],
        ]));

        var table = XlsxTableReader.Read(bytes, Limits()).Value!.Tables.Single();

        table.Headers.Should().Equal("AdSoyad", "Telefon");
        table.Rows.Select(r => r.RowNumber).Should().Equal(2, 4);
    }

    [Fact]
    public void Header_Only_Sheet_Should_Warn_And_Be_Skipped()
    {
        var bytes = ImportTestFiles.Xlsx(
            ("Musteriler", CustomerRows),
            ("Asilar", [["AsiAdi", "UygulamaTarihi"]]));

        var result = XlsxTableReader.Read(bytes, Limits()).Value!;

        result.Tables.Should().ContainSingle().Which.Table.Should().Be(ImportTable.Customers);
        result.Warnings.Should().ContainSingle(w => w.Code == "Imports.TableEmpty" && w.Table == ImportTable.Vaccinations);
    }

    [Fact]
    public void No_Data_Anywhere_Should_Be_Empty_File()
    {
        var bytes = ImportTestFiles.Xlsx(("Musteriler", [["AdSoyad"]]));

        XlsxTableReader.Read(bytes, Limits()).Error.Code.Should().Be("Imports.EmptyFile");
    }

    [Fact]
    public void No_Recognized_Sheet_Should_Be_Unsupported()
    {
        var bytes = ImportTestFiles.Xlsx(("Sayfa1", CustomerRows));

        XlsxTableReader.Read(bytes, Limits()).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Duplicate_Sheets_For_Same_Table_Should_Be_Rejected()
    {
        var bytes = ImportTestFiles.Xlsx(("Musteriler", CustomerRows), ("Customers", CustomerRows));

        XlsxTableReader.Read(bytes, Limits()).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Too_Many_Rows_Per_Table_Should_Be_Rejected()
    {
        var rows = new List<object?[]> { new object?[] { "AdSoyad" } };
        rows.AddRange(Enumerable.Range(0, 6).Select(i => new object?[] { $"Kişi {i}" }));
        var bytes = ImportTestFiles.Xlsx(("Musteriler", rows.ToArray()));

        XlsxTableReader.Read(bytes, Limits(l => l.MaxRowsPerTable = 5)).Error.Code.Should().Be("Imports.TooManyRows");
    }

    [Fact]
    public void Too_Many_Total_Rows_Should_Be_Rejected()
    {
        var bytes = ImportTestFiles.Xlsx(
            ("Musteriler", [["AdSoyad"], ["A"], ["B"], ["C"]]),
            ("Hayvanlar", [["HayvanAdi"], ["D"], ["E"], ["F"]]));

        XlsxTableReader.Read(bytes, Limits(l => l.MaxTotalRows = 5)).Error.Code.Should().Be("Imports.TooManyRows");
    }

    [Fact]
    public void Too_Many_Columns_Should_Be_Rejected()
    {
        var header = Enumerable.Range(0, 5).Select(i => (object?)$"S{i}").ToArray();
        var bytes = ImportTestFiles.Xlsx(("Musteriler", [header, header]));

        XlsxTableReader.Read(bytes, Limits(l => l.MaxColumns = 4)).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Too_Many_Sheets_Should_Be_Rejected()
    {
        var sheets = Enumerable.Range(1, 4).Select(i => ($"Sayfa{i}", CustomerRows)).Append(("Musteriler", CustomerRows)).ToArray();
        var bytes = ImportTestFiles.Xlsx(sheets);

        XlsxTableReader.Read(bytes, Limits(l => l.MaxSheets = 3)).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Too_Long_Cell_Should_Be_Rejected()
    {
        var bytes = ImportTestFiles.Xlsx(("Musteriler", [["AdSoyad"], [new string('a', 40)]]));

        XlsxTableReader.Read(bytes, Limits(l => l.MaxCellChars = 30)).Error.Code.Should().Be("Imports.FileTooLarge");
    }

    [Fact]
    public void Garbage_With_Zip_Signature_Should_Be_Unsupported()
    {
        byte[] bytes = [.. new byte[] { 0x50, 0x4B, 0x03, 0x04 }, .. Encoding.ASCII.GetBytes("bozuk veri bozuk veri")];

        XlsxTableReader.Read(bytes, Limits()).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Truncated_Package_Should_Be_Unsupported()
    {
        var valid = CustomersOnly();

        XlsxTableReader.Read(valid[..(valid.Length / 2)], Limits()).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Zip_Without_Workbook_Should_Be_Unsupported()
    {
        var bytes = ImportTestFiles.WithEntry(CustomersOnly(), "xl/workbook.xml", Encoding.UTF8.GetBytes("x"));
        bytes = ImportTestFiles.WithEntry(bytes, "[Content_Types].xml", Encoding.UTF8.GetBytes("x"));
        // Parçalar var ama geçersiz XML: ClosedXML okuyamaz.
        XlsxTableReader.Read(bytes, Limits()).Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Macro_Enabled_Package_Should_Be_Rejected()
    {
        var bytes = ImportTestFiles.WithEntry(CustomersOnly(), "xl/vbaProject.bin", [1, 2, 3]);

        var result = XlsxTableReader.Read(bytes, Limits());

        result.Error.Code.Should().Be("Imports.UnsupportedFile");
        result.Error.Message.Should().Contain("Makro");
    }

    [Fact]
    public void Doctype_Or_Entity_In_Xml_Part_Should_Be_Rejected_As_Xxe()
    {
        var sheetXml = Encoding.UTF8.GetString(ImportTestFiles.ReadEntry(CustomersOnly(), "xl/worksheets/sheet1.xml"));
        var evil = sheetXml.Replace(
            "<x:worksheet",
            "<!DOCTYPE worksheet [<!ENTITY xxe SYSTEM \"file:///c:/windows/win.ini\">]><x:worksheet",
            StringComparison.Ordinal);
        evil.Should().Contain("DOCTYPE");
        var bytes = ImportTestFiles.WithEntry(CustomersOnly(), "xl/worksheets/sheet1.xml", Encoding.UTF8.GetBytes(evil));

        var result = XlsxTableReader.Read(bytes, Limits());

        result.Error.Code.Should().Be("Imports.UnsupportedFile");
        result.Error.Message.Should().Contain("DTD");
    }

    [Fact]
    public void Zip_Bomb_Should_Be_Rejected_By_Real_Uncompressed_Size()
    {
        // 5 MB sıfır baytı birkaç KB'a sıkışır; ClosedXML'e verilmeden açılmış boyut tavanına takılır.
        var bytes = ImportTestFiles.WithEntry(CustomersOnly(), "xl/media/bomb.bin", new byte[5 * 1024 * 1024]);
        bytes.Length.Should().BeLessThan(100 * 1024);

        var result = XlsxTableReader.Read(bytes, Limits(l => l.MaxUncompressedBytes = 1024 * 1024));

        result.Error.Code.Should().Be("Imports.FileTooLarge");
    }

    [Fact]
    public void Too_Many_Zip_Entries_Should_Be_Rejected()
    {
        var bytes = CustomersOnly();
        for (var i = 0; i < 5; i++)
            bytes = ImportTestFiles.WithEntry(bytes, $"xl/extra/{i}.txt", [1]);

        XlsxTableReader.Read(bytes, Limits(l => l.MaxZipEntries = 5)).Error.Code.Should().Be("Imports.UnsupportedFile");
    }
}
