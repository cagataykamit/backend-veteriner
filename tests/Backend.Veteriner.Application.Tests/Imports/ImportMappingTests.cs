using Backend.Veteriner.Application.Imports;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

public sealed class ImportMappingTests
{
    [Theory]
    [InlineData("Müşteri No", "musterino")]
    [InlineData("MÜŞTERİ-NO", "musterino")]
    [InlineData("  Mikroçip ", "mikrocip")]
    [InlineData("Işık", "isik")]
    [InlineData("IŞIK", "isik")]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("Kuduz-2023", "kuduz2023")]
    [InlineData("kuduz_2023", "kuduz2023")]
    [InlineData("Ağırlık (kg)", "agirlikkg")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Fold_Should_Normalize_Turkish_Text(string? input, string expected)
        => ImportTextFolding.Fold(input).Should().Be(expected);

    [Fact]
    public void Fold_Should_Match_Daily_Search_Dotless_I_Rule()
    {
        // ı→i, İ→I (DailySearchTerm.Fold) ardından küçük harf: tek katlama kuralı.
        ImportTextFolding.Fold("ISPARTA").Should().Be(ImportTextFolding.Fold("ıspartA"));
    }

    [Fact]
    public void Suggest_Should_Prefer_Exact_Over_Alias_And_Claim_Each_Field_Once()
    {
        var headers = new[] { "Ad Soyad", "Cep", "E-posta", "Telefon", "Bilinmeyen", "Açık Adres" };

        var suggestions = ImportCanonicalSchema.Suggest(ImportTable.Customers, headers);

        suggestions.Select(s => (s.Source, s.Suggested, s.Confidence)).Should().Equal(
            ("Ad Soyad", "AdSoyad", ImportSuggestionConfidence.Exact),
            // "Cep" takma ad; ama "Telefon" kanonik adla tam eşleşir ve önce Exact turunda alanı alır.
            ("Cep", null, ImportSuggestionConfidence.None),
            ("E-posta", "Eposta", ImportSuggestionConfidence.Exact),
            ("Telefon", "Telefon", ImportSuggestionConfidence.Exact),
            ("Bilinmeyen", null, ImportSuggestionConfidence.None),
            ("Açık Adres", "Adres", ImportSuggestionConfidence.Alias));
    }

    [Fact]
    public void Suggest_Should_Use_Alias_When_No_Exact_Match()
    {
        var suggestions = ImportCanonicalSchema.Suggest(ImportTable.Customers, ["Müşteri Adı", "GSM"]);

        suggestions.Select(s => (s.Suggested, s.Confidence)).Should().Equal(
            ("AdSoyad", ImportSuggestionConfidence.Alias),
            ("Telefon", ImportSuggestionConfidence.Alias));
    }

    [Fact]
    public void Suggest_Should_Give_Field_To_First_Of_Equal_Candidates()
    {
        var suggestions = ImportCanonicalSchema.Suggest(ImportTable.Customers, ["Tel", "Cep"]);

        suggestions[0].Suggested.Should().Be("Telefon");
        suggestions[1].Suggested.Should().BeNull();
    }

    [Theory]
    [InlineData("Mikroçip", "Mikrocip")]
    [InlineData("MIKROCIP", "Mikrocip")]
    [InlineData("Doğum Tarihi", "DogumTarihi")]
    [InlineData("Hayvan Adı", "HayvanAdi")]
    [InlineData("Tür", "Tur")]
    [InlineData("Irk", "Irk")]
    [InlineData("Kısır", "Kisir")]
    public void Pet_Headers_Should_Match_Turkish_Variants(string header, string expected)
    {
        var suggestion = ImportCanonicalSchema.Suggest(ImportTable.Pets, [header]).Single();

        suggestion.Suggested.Should().Be(expected);
    }

    [Fact]
    public void Template_Headers_Should_Map_Exactly_For_Every_Table()
    {
        foreach (var table in ImportCanonicalNames.Tables)
        {
            var names = ImportCanonicalSchema.Fields(table).Select(f => f.Name).ToList();

            var suggestions = ImportCanonicalSchema.Suggest(table, names);

            suggestions.Should().OnlyContain(s => s.Confidence == ImportSuggestionConfidence.Exact);
            suggestions.Select(s => s.Suggested).Should().Equal(names);
        }
    }

    [Fact]
    public void Canonical_Field_Names_Should_Be_Unique_Per_Table()
    {
        foreach (var table in ImportCanonicalNames.Tables)
            ImportCanonicalSchema.Fields(table).Select(f => f.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Sheet_Names_Should_Resolve_Case_And_Accent_Insensitively()
    {
        ImportCanonicalSchema.TryResolveSheet("MÜŞTERİLER", out var customers).Should().BeTrue();
        customers.Should().Be(ImportTable.Customers);
        ImportCanonicalSchema.TryResolveSheet("Hastalar", out var pets).Should().BeTrue();
        pets.Should().Be(ImportTable.Pets);
        ImportCanonicalSchema.TryResolveSheet("aşılar", out var vaccinations).Should().BeTrue();
        vaccinations.Should().Be(ImportTable.Vaccinations);
        ImportCanonicalSchema.TryResolveSheet("Aciklama", out _).Should().BeFalse();
    }

    [Fact]
    public void MissingRequired_Should_Report_All_Of_And_Any_Of_Groups()
    {
        ImportCanonicalSchema.MissingRequired(ImportTable.Customers, ["AdSoyad", "Telefon"]).Should().BeEmpty();
        ImportCanonicalSchema.MissingRequired(ImportTable.Customers, ["Telefon"]).Should().Equal("AdSoyad");

        // Hasta: MusteriNo VEYA SahipAdSoyad+SahipTelefon.
        ImportCanonicalSchema.MissingRequired(ImportTable.Pets, ["HayvanAdi", "Tur", "MusteriNo"]).Should().BeEmpty();
        ImportCanonicalSchema.MissingRequired(ImportTable.Pets, ["HayvanAdi", "Tur", "SahipAdSoyad", "SahipTelefon"]).Should().BeEmpty();
        ImportCanonicalSchema.MissingRequired(ImportTable.Pets, ["HayvanAdi", "Tur", "SahipAdSoyad"])
            .Should().ContainSingle().Which.Should().Contain("MusteriNo").And.Contain("SahipAdSoyad + SahipTelefon");
        // Okunabilir liste şablon başlığıyla (ADR-022 "hasta"); girdi API anahtarıdır.
        ImportCanonicalSchema.MissingRequired(ImportTable.Pets, ["MusteriNo"]).Should().Equal("HastaAdi", "Tur");

        // Aşı: HayvanNo VEYA SahipAdSoyad+SahipTelefon+HayvanAdi+Tur.
        ImportCanonicalSchema.MissingRequired(ImportTable.Vaccinations, ["AsiAdi", "UygulamaTarihi", "HayvanNo"]).Should().BeEmpty();
        ImportCanonicalSchema.MissingRequired(
            ImportTable.Vaccinations,
            ["AsiAdi", "UygulamaTarihi", "SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur"]).Should().BeEmpty();
        ImportCanonicalSchema.MissingRequired(ImportTable.Vaccinations, ["AsiAdi", "UygulamaTarihi", "SahipAdSoyad", "SahipTelefon"])
            .Should().ContainSingle().Which.Should().Be("HastaNo veya SahipAdSoyad + SahipTelefon + HastaAdi + Tur");
    }

    [Theory]
    // Eski şablon başlıkları (API anahtarı) kesin eşleşmeye devam eder.
    [InlineData(ImportTable.Pets, "HayvanNo", "HayvanNo", ImportSuggestionConfidence.Exact)]
    [InlineData(ImportTable.Pets, "HayvanAdi", "HayvanAdi", ImportSuggestionConfidence.Exact)]
    [InlineData(ImportTable.Vaccinations, "HAYVANNO", "HayvanNo", ImportSuggestionConfidence.Exact)]
    [InlineData(ImportTable.Vaccinations, "hayvan_adi", "HayvanAdi", ImportSuggestionConfidence.Exact)]
    // Yeni şablon başlıkları (Türkçe/aksan duyarsız katlama aynı).
    [InlineData(ImportTable.Pets, "HastaNo", "HayvanNo", ImportSuggestionConfidence.Exact)]
    [InlineData(ImportTable.Pets, "HASTA ADI", "HayvanAdi", ImportSuggestionConfidence.Exact)]
    [InlineData(ImportTable.Vaccinations, "Hasta-No", "HayvanNo", ImportSuggestionConfidence.Exact)]
    [InlineData(ImportTable.Vaccinations, "hastaadı", "HayvanAdi", ImportSuggestionConfidence.Exact)]
    // Eski elle yazılmış başlıklar takma ad olarak kalır.
    [InlineData(ImportTable.Pets, "Hayvan No", "HayvanNo", ImportSuggestionConfidence.Exact)]
    [InlineData(ImportTable.Pets, "Hayvan Numarası", "HayvanNo", ImportSuggestionConfidence.Alias)]
    [InlineData(ImportTable.Vaccinations, "Hayvan", "HayvanAdi", ImportSuggestionConfidence.Alias)]
    [InlineData(ImportTable.Vaccinations, "Hasta", "HayvanAdi", ImportSuggestionConfidence.Alias)]
    public void Pet_Number_And_Name_Should_Accept_Old_And_New_Headers(
        ImportTable table, string header, string expectedKey, ImportSuggestionConfidence expectedConfidence)
    {
        var suggestion = ImportCanonicalSchema.Suggest(table, [header]).Single();

        suggestion.Suggested.Should().Be(expectedKey);
        suggestion.Confidence.Should().Be(expectedConfidence);
    }

    [Fact]
    public void Mapping_Document_Should_Round_Trip_And_Tolerate_Garbage()
    {
        var targetId = Guid.NewGuid();
        var doc = new ImportMappingDocument
        {
            Columns = { ["customers"] = new Dictionary<string, string> { ["AdSoyad"] = "Ad Soyad" } },
            ValueMappings = { new ImportValueMapping("species", "Kopek", targetId), new ImportValueMapping("vaccine", "X", null) },
        };

        var parsed = ImportMappingDocument.Parse(doc.ToJson());

        parsed.ColumnsOf(ImportTable.Customers).Should().Equal(new Dictionary<string, string> { ["AdSoyad"] = "Ad Soyad" });
        parsed.ColumnsOf(ImportTable.Pets).Should().BeEmpty();
        parsed.ValueMappings.Should().Equal(doc.ValueMappings);
        ImportMappingDocument.Parse(null).Columns.Should().BeEmpty();
        ImportMappingDocument.Parse("{bozuk").Columns.Should().BeEmpty();
    }

    [Fact]
    public void Table_Info_Should_Show_Suggestions_And_Saved_Mapping()
    {
        var batch = ImportBatch.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(), DateTime.UtcNow).Value!;
        batch.AttachFile(new string('a', 64), 2, 0, 0);
        var headers = new Dictionary<ImportTable, IReadOnlyList<string>> { [ImportTable.Customers] = ["Ad Soyad", "Cep"] };

        var before = ImportTableInfoLoader.Build(batch, headers).Single();
        before.Table.Should().Be("customers");
        before.RowCount.Should().Be(2);
        before.Columns.Select(c => (c.Source, c.Suggested, c.Mapped)).Should().Equal(
            ("Ad Soyad", "AdSoyad", null), ("Cep", "Telefon", null));

        var mapping = new ImportMappingDocument
        {
            Columns = { ["customers"] = new Dictionary<string, string> { ["AdSoyad"] = "Ad Soyad" } },
        };
        batch.ApplyMapping(mapping.ToJson(), null, DateTime.UtcNow);

        var after = ImportTableInfoLoader.Build(batch, headers).Single();
        after.Columns.Select(c => (c.Source, c.Mapped)).Should().Equal(("Ad Soyad", "AdSoyad"), ("Cep", null));
    }

    [Fact]
    public void Table_Info_Should_List_Only_Tables_With_Rows_And_Keep_Rowcount_Without_Headers()
    {
        var batch = ImportBatch.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(), DateTime.UtcNow).Value!;
        batch.AttachFile(new string('a', 64), 0, 7, 0);

        var info = ImportTableInfoLoader.Build(batch, new Dictionary<ImportTable, IReadOnlyList<string>>());

        info.Should().ContainSingle();
        info[0].Table.Should().Be("pets");
        info[0].RowCount.Should().Be(7);
        info[0].Columns.Should().BeEmpty();
    }

    [Fact]
    public void Headers_Should_Be_Read_From_Source_Json_In_File_Order()
    {
        ImportTableInfoLoader.HeadersOf("{\"Ad\":\"x\",\"Boş\":null,\"Tel\":\"1\"}").Should().Equal("Ad", "Boş", "Tel");
        ImportTableInfoLoader.HeadersOf(null).Should().BeEmpty();
        ImportTableInfoLoader.HeadersOf("[1]").Should().BeEmpty();
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+90 532", "'+90 532")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tx", "'\tx")]
    [InlineData("Ayşe", "Ayşe")]
    [InlineData("", "")]
    [InlineData("a=b", "a=b")]
    public void ForExport_Should_Prefix_Formula_Triggers(string input, string expected)
        => ImportCellSafety.ForExport(input).Should().Be(expected);
}
