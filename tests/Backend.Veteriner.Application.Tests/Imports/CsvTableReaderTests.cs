using System.Text;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Domain.Imports;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

public sealed class CsvTableReaderTests
{
    private static ImportFileLimits Limits(Action<ImportFileLimits>? configure = null) => ImportTestFiles.Limits(configure);

    private static Backend.Veteriner.Domain.Shared.Result<ImportSourceTable?> Read(
        byte[] bytes,
        string? encoding = null,
        string? delimiter = null,
        List<ImportReadWarning>? warnings = null,
        ImportFileLimits? limits = null)
        => CsvTableReader.Read(ImportTable.Customers, bytes, encoding, delimiter, limits ?? Limits(), warnings ?? []);

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    static CsvTableReaderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Theory]
    [InlineData(";")]
    [InlineData(",")]
    [InlineData("\t")]
    public void Delimiter_Should_Be_Detected(string d)
    {
        var text = $"Ad Soyad{d}Telefon{d}Eposta\r\nAyşe Yılmaz{d}05321234567{d}a@example.com\r\nVeli Kaya{d}05331234567{d}\r\n";

        var table = Read(Utf8(text)).Value!;

        table.Headers.Should().Equal("Ad Soyad", "Telefon", "Eposta");
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells.Should().Equal("Ayşe Yılmaz", "05321234567", "a@example.com");
        table.Rows[1].Cells.Should().Equal("Veli Kaya", "05331234567", null);
    }

    [Fact]
    public void Delimiter_Should_Prefer_Consistent_Field_Count_Over_Commas_In_Text()
    {
        // ';' ayırıcılı dosyada adres içindeki virgül ayırıcı sayılmamalı.
        var text = "Ad;Adres;Telefon\nAli;Örnek Mah., No:1;0532\nVeli;Başka Sk., No:2;0533\n";

        var table = Read(Utf8(text)).Value!;

        table.Headers.Should().Equal("Ad", "Adres", "Telefon");
        table.Rows[0].Cells[1].Should().Be("Örnek Mah., No:1");
    }

    [Fact]
    public void Forced_Delimiter_Should_Override_Detection()
    {
        var table = Read(Utf8("A,B;C\n1,2;3\n"), delimiter: ",").Value!;

        table.Headers.Should().Equal("A", "B;C");
    }

    [Fact]
    public void Utf8_Bom_Should_Be_Stripped()
    {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Utf8("Ad Soyad;Telefon\nÇağlar Öz;1\n")];

        var table = Read(bytes).Value!;

        table.Headers[0].Should().Be("Ad Soyad");
        table.Rows[0].Cells[0].Should().Be("Çağlar Öz");
    }

    [Fact]
    public void Invalid_Utf8_Should_Fall_Back_To_Windows1254()
    {
        var bytes = Encoding.GetEncoding(1254).GetBytes("Ad Soyad;Şehir\nİsmail Işık;Şanlıurfa\n");
        var warnings = new List<ImportReadWarning>();

        var table = Read(bytes, warnings: warnings).Value!;

        table.Rows[0].Cells.Should().Equal("İsmail Işık", "Şanlıurfa");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Forced_Wrong_Encoding_Should_Warn_Encoding_Suspect()
    {
        var bytes = Encoding.GetEncoding(1254).GetBytes("Ad Soyad;Şehir\nİsmail Işık;Şanlıurfa\n");
        var warnings = new List<ImportReadWarning>();

        Read(bytes, encoding: "utf-8", warnings: warnings).IsSuccess.Should().BeTrue();

        warnings.Should().ContainSingle(w => w.Code == "Imports.EncodingSuspect" && w.Table == ImportTable.Customers);
    }

    [Fact]
    public void Forced_Windows1254_Should_Decode_Turkish_Letters()
    {
        var bytes = Encoding.GetEncoding(1254).GetBytes("Ad Soyad;Not\nGülşen;ığüşöç\n");

        var table = Read(bytes, encoding: "windows-1254").Value!;

        table.Rows[0].Cells.Should().Equal("Gülşen", "ığüşöç");
    }

    [Fact]
    public void Quoted_Fields_Should_Follow_Rfc4180()
    {
        var text = "Ad;Not\n\"Ali; Veli\";\"Satır 1\nSatır 2\"\n\"Tırnak \"\"içinde\"\"\";x\n";

        var table = Read(Utf8(text)).Value!;

        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells.Should().Equal("Ali; Veli", "Satır 1\nSatır 2");
        table.Rows[1].Cells[0].Should().Be("Tırnak \"içinde\"");
    }

    [Fact]
    public void Blank_Rows_Should_Be_Skipped_But_Counted_In_Row_Numbers()
    {
        var table = Read(Utf8("Ad;Tel\n\nAli;1\n;\nVeli;2")).Value!;

        table.Rows.Select(r => r.RowNumber).Should().Equal(3, 5);
        table.Rows.Select(r => r.Cells[0]).Should().Equal("Ali", "Veli");
    }

    [Fact]
    public void Leading_Blank_Lines_Should_Be_Skipped_Before_Header()
    {
        var table = Read(Utf8("\n\nAd;Tel\nAli;1\n")).Value!;

        table.Headers.Should().Equal("Ad", "Tel");
        table.Rows.Should().ContainSingle().Which.RowNumber.Should().Be(4);
    }

    [Fact]
    public void Formula_Like_Text_Should_Be_Kept_As_Plain_Text()
    {
        var text = "Ad;Tel;Not;Ek\n=1+1;+90 532 123 45 67;-5;@SUM(A1)\n";

        var row = Read(Utf8(text)).Value!.Rows.Single();

        row.Cells.Should().Equal("=1+1", "+90 532 123 45 67", "-5", "@SUM(A1)");
    }

    [Fact]
    public void Duplicate_And_Blank_Headers_Should_Be_Made_Unique()
    {
        var table = Read(Utf8("Ad;Ad;;\nA;B;C;\nD;E;F;\n")).Value!;

        table.Headers.Should().Equal("Ad", "Ad (2)", "Sütun 3");
        table.Rows[0].Cells.Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Empty_File_Without_Header_Should_Return_Null_Table()
    {
        Read(Utf8("\n\n  \n")).Value.Should().BeNull();
    }

    [Fact]
    public void Header_Only_File_Should_Have_No_Rows()
    {
        var table = Read(Utf8("Ad;Tel\n")).Value!;

        table.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Too_Many_Rows_Should_Be_Rejected_While_Reading()
    {
        var text = "Ad\n" + string.Join('\n', Enumerable.Range(0, 6).Select(i => $"Kişi {i}"));

        var result = Read(Utf8(text), limits: Limits(l => l.MaxRowsPerTable = 5));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Imports.TooManyRows");
    }

    [Fact]
    public void Row_Limit_Should_Allow_Exactly_The_Maximum()
    {
        var text = "Ad\n" + string.Join('\n', Enumerable.Range(0, 5).Select(i => $"Kişi {i}"));

        Read(Utf8(text), limits: Limits(l => l.MaxRowsPerTable = 5)).Value!.Rows.Should().HaveCount(5);
    }

    [Fact]
    public void Too_Many_Columns_Should_Be_Rejected()
    {
        var header = string.Join(';', Enumerable.Range(0, 4).Select(i => $"S{i}"));

        var result = Read(Utf8(header + "\n1;2;3;4\n"), limits: Limits(l => l.MaxColumns = 3));

        result.Error.Code.Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Too_Long_Cell_Should_Be_Rejected()
    {
        var result = Read(Utf8("Ad\n" + new string('a', 51)), limits: Limits(l => l.MaxCellChars = 50));

        result.Error.Code.Should().Be("Imports.FileTooLarge");
    }

    [Fact]
    public void Long_Header_Should_Be_Truncated()
    {
        var table = Read(Utf8(new string('h', 250) + "\nx\n")).Value!;

        table.Headers[0].Should().HaveLength(200);
    }

    [Fact]
    public void Single_Column_File_Should_Default_To_Semicolon()
    {
        var table = Read(Utf8("Ad Soyad\nAli, Veli\n")).Value!;

        table.Headers.Should().Equal("Ad Soyad");
        table.Rows[0].Cells[0].Should().Be("Ali, Veli");
    }

    [Theory]
    [InlineData("utf-8", true)]
    [InlineData("UTF8", true)]
    [InlineData("windows-1254", false)]
    [InlineData("cp1254", false)]
    [InlineData("iso-8859-9", false)]
    public void Known_Encodings_Should_Parse(string value, bool utf8)
    {
        CsvTableReader.TryParseEncoding(value, out var parsed).Should().BeTrue();
        parsed.Should().Be(utf8);
    }

    [Theory]
    [InlineData("latin1")]
    [InlineData("utf-16")]
    public void Unknown_Encodings_Should_Be_Rejected(string value)
        => CsvTableReader.TryParseEncoding(value, out _).Should().BeFalse();

    [Theory]
    [InlineData(";", ';')]
    [InlineData("comma", ',')]
    [InlineData("tab", '\t')]
    [InlineData("\t", '\t')]
    public void Known_Delimiters_Should_Parse(string value, char expected)
    {
        CsvTableReader.TryParseDelimiter(value, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Fact]
    public void Unknown_Delimiter_Should_Be_Rejected()
        => CsvTableReader.TryParseDelimiter("|", out _).Should().BeFalse();
}
