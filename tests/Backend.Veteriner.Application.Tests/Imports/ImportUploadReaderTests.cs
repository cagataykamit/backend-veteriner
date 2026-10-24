using System.Text;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Imports;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

public sealed class ImportUploadReaderTests
{
    private static readonly byte[] ValidXlsx = ImportTestFiles.Xlsx(("Musteriler", [["AdSoyad", "Telefon"], ["Ayşe", "0532"]]));

    private static ImportFileSet Xlsx(ImportUploadedFile file) => new(file, null, null, null);

    private static ImportFileSet Csv(
        ImportUploadedFile? customers = null,
        ImportUploadedFile? pets = null,
        ImportUploadedFile? vaccinations = null)
        => new(null, customers, pets, vaccinations);

    private static string CodeOf(ImportFileSet files, Action<ImportFileLimits>? configure = null)
    {
        var result = ImportTestFiles.Reader(configure).Prepare(files, null, null);
        result.IsSuccess.Should().BeFalse();
        return result.Error.Code;
    }

    [Fact]
    public void No_Files_Should_Be_Empty_File()
        => CodeOf(new ImportFileSet(null, null, null, null)).Should().Be("Imports.EmptyFile");

    [Fact]
    public void Zero_Byte_File_Should_Be_Empty_File()
        => CodeOf(Xlsx(ImportTestFiles.XlsxFile([]))).Should().Be("Imports.EmptyFile");

    [Fact]
    public void Workbook_And_Csv_Fields_Cannot_Be_Combined()
        => CodeOf(new ImportFileSet(ImportTestFiles.XlsxFile(ValidXlsx), ImportTestFiles.Csv("A\n1"), null, null))
            .Should().Be("Imports.UnsupportedFile");

    [Theory]
    [InlineData("veri.xls")]
    [InlineData("veri.xlsm")]
    [InlineData("veri.csv")]
    [InlineData("veri")]
    public void Workbook_Field_Should_Accept_Only_Xlsx_Extension(string name)
        => CodeOf(Xlsx(ImportTestFiles.XlsxFile(ValidXlsx, name))).Should().Be("Imports.UnsupportedFile");

    [Theory]
    [InlineData("veri.xlsx")]
    [InlineData("veri.txt")]
    [InlineData("veri.xls")]
    public void Csv_Fields_Should_Accept_Only_Csv_Extension(string name)
        => CodeOf(Csv(customers: ImportTestFiles.Csv("A;B\n1;2", name))).Should().Be("Imports.UnsupportedFile");

    [Fact]
    public void Extension_Spoofing_Should_Be_Caught_By_Signature()
    {
        // .xlsx adlı düz metin ve .csv adlı zip.
        CodeOf(Xlsx(ImportTestFiles.XlsxFile(Encoding.UTF8.GetBytes("A;B\n1;2")))).Should().Be("Imports.UnsupportedFile");
        CodeOf(Csv(customers: new ImportUploadedFile("veri.csv", "text/csv", ValidXlsx))).Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Legacy_Or_Password_Protected_Ole_Signature_Should_Be_Rejected()
    {
        byte[] ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0];

        var result = ImportTestFiles.Reader().Prepare(Xlsx(ImportTestFiles.XlsxFile(ole)), null, null);

        result.Error.Code.Should().Be("Imports.UnsupportedFile");
        result.Error.Message.Should().Contain("parolalı");
        CodeOf(Csv(customers: new ImportUploadedFile("veri.csv", "text/csv", ole))).Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Binary_Content_In_Csv_Should_Be_Rejected()
    {
        CodeOf(Csv(customers: new ImportUploadedFile("veri.csv", "text/csv", [1, 2, 0, 3, 4]))).Should().Be("Imports.UnsupportedFile");
        CodeOf(Csv(customers: new ImportUploadedFile("veri.csv", "text/csv", Encoding.ASCII.GetBytes("%PDF-1.7 ...")))).Should().Be("Imports.UnsupportedFile");
    }

    [Fact]
    public void Utf16_Csv_With_Bom_Should_Not_Be_Mistaken_For_Binary()
    {
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Ad;Tel\nAli;1\n")];

        var result = ImportTestFiles.Reader().Prepare(Csv(customers: new ImportUploadedFile("veri.csv", "text/csv", bytes)), null, null);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    public void Unexpected_Content_Type_Should_Be_Rejected(string contentType)
    {
        CodeOf(Xlsx(ImportTestFiles.XlsxFile(ValidXlsx, type: contentType))).Should().Be("Imports.UnsupportedFile");
        CodeOf(Csv(customers: new ImportUploadedFile("veri.csv", contentType, Encoding.UTF8.GetBytes("A;B\n1;2"))))
            .Should().Be("Imports.UnsupportedFile");
    }

    [Theory]
    [InlineData("text/csv; charset=utf-8")]
    [InlineData("TEXT/CSV")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public void Common_Csv_Content_Types_Should_Be_Accepted(string? contentType)
    {
        var file = new ImportUploadedFile("veri.csv", contentType, Encoding.UTF8.GetBytes("A;B\n1;2"));

        ImportTestFiles.Reader().Prepare(Csv(customers: file), null, null).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Oversized_File_Should_Be_Rejected_Before_Parsing()
        => CodeOf(Xlsx(ImportTestFiles.XlsxFile(ValidXlsx)), l => l.MaxFileBytes = 100).Should().Be("Imports.FileTooLarge");

    [Fact]
    public void Workbook_Hash_Should_Be_Sha256_Of_File_Bytes()
    {
        var hash = ImportTestFiles.Reader().Prepare(Xlsx(ImportTestFiles.XlsxFile(ValidXlsx)), null, null).Value!.ContentHash;

        hash.Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(ValidXlsx)));
        hash.Should().HaveLength(ImportBatch.ContentHashLength);
    }

    [Fact]
    public void Csv_Hash_Should_Depend_On_Content_And_Table_But_Not_On_File_Name_Or_Order()
    {
        var reader = ImportTestFiles.Reader();
        var customers = ImportTestFiles.Csv("A;B\n1;2", "a.csv");
        var pets = ImportTestFiles.Csv("C;D\n3;4", "b.csv");

        string Hash(ImportFileSet set) => reader.Prepare(set, null, null).Value!.ContentHash;

        var both = Hash(Csv(customers, pets));
        both.Should().Be(Hash(Csv(customers with { FileName = "baska.csv" }, pets)));
        both.Should().NotBe(Hash(Csv(customers)));
        // Aynı içerik farklı tablo rolünde farklı özet verir.
        Hash(Csv(customers: customers)).Should().NotBe(Hash(Csv(pets: customers)));
    }

    [Fact]
    public void Read_Should_Combine_Csv_Tables_And_Warn_About_Empty_Ones()
    {
        var reader = ImportTestFiles.Reader();
        var files = Csv(
            customers: ImportTestFiles.Csv("AdSoyad;Telefon\nAli;1\nVeli;2\n"),
            pets: ImportTestFiles.Csv("HayvanAdi;Tur\n"),
            vaccinations: ImportTestFiles.Csv("AsiAdi\nKuduz\n"));

        var read = reader.Read(reader.Prepare(files, null, null).Value!, trialTenant: false);

        read.IsSuccess.Should().BeTrue(read.Error.Message);
        read.Value!.Tables.Select(t => (t.Table, t.Rows.Count)).Should().Equal(
            (ImportTable.Customers, 2), (ImportTable.Vaccinations, 1));
        read.Value.Warnings.Should().ContainSingle(w => w.Code == "Imports.TableEmpty" && w.Table == ImportTable.Pets);
    }

    [Fact]
    public void Read_Should_Reject_When_Every_Csv_Is_Header_Only()
    {
        var reader = ImportTestFiles.Reader();
        var files = Csv(customers: ImportTestFiles.Csv("AdSoyad;Telefon\n"));

        reader.Read(reader.Prepare(files, null, null).Value!, trialTenant: false).Error.Code.Should().Be("Imports.EmptyFile");
    }

    [Fact]
    public void Read_Should_Enforce_Total_Row_Limit_Across_Csv_Tables()
    {
        var reader = ImportTestFiles.Reader(l => l.MaxTotalRows = 3);
        var files = Csv(
            customers: ImportTestFiles.Csv("AdSoyad\nA\nB\n"),
            pets: ImportTestFiles.Csv("HayvanAdi\nC\nD\n"));

        reader.Read(reader.Prepare(files, null, null).Value!, trialTenant: false).Error.Code.Should().Be("Imports.TooManyRows");
    }

    [Fact]
    public void Read_Should_Pass_Forced_Encoding_And_Delimiter_To_Csv()
    {
        var reader = ImportTestFiles.Reader();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1254).GetBytes("Ad;Şehir\nİsmail;Işık\n");
        var files = Csv(customers: new ImportUploadedFile("veri.csv", "text/csv", bytes));

        var read = reader.Read(reader.Prepare(files, "windows-1254", "semicolon").Value!, trialTenant: false);

        var table = read.Value!.Tables.Single();
        table.Headers.Should().Equal("Ad", "Şehir");
        table.Rows.Single().Cells.Should().Equal("İsmail", "Işık");
    }

    [Fact]
    public void Read_Should_Apply_Trial_RowsPerTable_Limit_With_Trial_Code()
    {
        // TRIAL-004: deneme kiracısında tablo başına 500 satır; yapılandırılmış 10.000 tavanı devre dışı kalır.
        var reader = ImportTestFiles.Reader();
        var rows = string.Join('\n', Enumerable.Range(1, TrialLimits.ImportMaxRowsPerTable + 1).Select(i => $"Musteri {i}"));
        var files = Csv(customers: ImportTestFiles.Csv($"AdSoyad\n{rows}\n"));
        var prepared = reader.Prepare(files, null, null).Value!;

        var trial = reader.Read(prepared, trialTenant: true);
        var paid = reader.Read(prepared, trialTenant: false);

        trial.IsSuccess.Should().BeFalse();
        trial.Error.Code.Should().Be("Trial.ImportLimitExceeded");
        trial.Error.Message.Should().Contain("Deneme sürümünde").And.Contain("abonelikle kalkar");
        paid.IsSuccess.Should().BeTrue(paid.Error.Message);
        paid.Value!.Tables.Single().Rows.Should().HaveCount(TrialLimits.ImportMaxRowsPerTable + 1);
    }

    [Fact]
    public void Read_Should_Keep_Configured_Code_When_Trial_Limit_Is_Not_Lower()
    {
        // Yapılandırılmış tavan deneme sınırından düşükse deneme kiracısı da yapılandırılmış tavanı ve kodu alır.
        var reader = ImportTestFiles.Reader(l => l.MaxRowsPerTable = 2);
        var files = Csv(customers: ImportTestFiles.Csv("AdSoyad\nA\nB\nC\n"));

        reader.Read(reader.Prepare(files, null, null).Value!, trialTenant: true).Error.Code.Should().Be("Trial.ImportLimitExceeded");
    }
}
