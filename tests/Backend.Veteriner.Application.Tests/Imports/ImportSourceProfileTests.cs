using Backend.Veteriner.Application.Imports;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Queries.GetSourceProfiles;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>TIER-021: kaynak program profili sütun önerisini genişletir; ikinci bir eşleme mekanizması yoktur.</summary>
public sealed class ImportSourceProfileTests
{
    [Fact]
    public void Profiles_Should_Map_Only_To_Canonical_Fields_And_Be_Unverified()
    {
        foreach (var profile in ImportSourceProfiles.All)
        {
            foreach (var mapping in profile.Mappings)
            {
                ImportCanonicalSchema.IsCanonical(mapping.Table, mapping.Field).Should().BeTrue($"{profile.Code} {mapping.Header}");
                mapping.Source.Should().NotBeNullOrWhiteSpace();
                mapping.Verified.Should().BeFalse("gerçek dışa aktarma dosyası görülmedi");
            }

            // Aynı tabloda aynı (katlanmış) başlık iki alana gidemez.
            profile.Mappings
                .GroupBy(m => (m.Table, ImportTextFolding.Fold(m.Header)))
                .Should().OnlyContain(g => g.Count() == 1);
            profile.Verified.Should().BeFalse();
        }
    }

    [Fact]
    public void Profiles_Should_Cover_Generic_Kolayvet_And_EVet_Once()
        => ImportSourceProfiles.All.Select(p => p.Code).Should()
            .Equal(ImportSourceProfile.Generic, ImportSourceProfile.Kolayvet, ImportSourceProfile.EVet);

    [Fact]
    public void Kolayvet_Should_Have_No_Mapping_Without_Source()
        => ImportSourceProfiles.Get(ImportSourceProfile.Kolayvet).Mappings.Should().BeEmpty();

    [Fact]
    public void EVet_Profile_Should_Suggest_Its_Headers_Before_General_Aliases()
    {
        var headers = new[] { "Adı", "Protokol No", "Gsm", "Email", "Kart No" };

        var generic = ImportCanonicalSchema.Suggest(ImportTable.Customers, headers);
        var evet = ImportCanonicalSchema.Suggest(ImportTable.Customers, headers, ImportSourceProfile.EVet);

        // Genel listede "Adı" müşteri adı değildir, "Protokol No" bilinmez.
        generic.Select(s => s.Suggested).Should().Equal(null, null, "Telefon", "Eposta", null);
        evet.Select(s => (s.Suggested, s.Confidence)).Should().Equal(
            ("AdSoyad", ImportSuggestionConfidence.Profile),
            ("MusteriNo", ImportSuggestionConfidence.Profile),
            ("Telefon", ImportSuggestionConfidence.Profile),
            ("Eposta", ImportSuggestionConfidence.Profile),
            (null, ImportSuggestionConfidence.None));
    }

    [Fact]
    public void Exact_Template_Header_Should_Still_Win_Over_Profile()
    {
        var suggestions = ImportCanonicalSchema.Suggest(ImportTable.Customers, ["Adı", "AdSoyad"], ImportSourceProfile.EVet);

        suggestions.Select(s => (s.Suggested, s.Confidence)).Should().Equal(
            (null, ImportSuggestionConfidence.None),
            ("AdSoyad", ImportSuggestionConfidence.Exact));
    }

    [Theory]
    [InlineData("HASTA IRKI")]
    [InlineData("hasta ırkı")]
    [InlineData("Hasta Irki")]
    [InlineData("HASTA-IRKI")]
    public void Profile_Headers_Should_Ignore_Case_And_Turkish_Letters(string header)
        => ImportCanonicalSchema.Suggest(ImportTable.Pets, [header], ImportSourceProfile.EVet).Single()
            .Should().Be(new ImportColumnSuggestion(header, "Irk", ImportSuggestionConfidence.Profile));

    [Fact]
    public void EVet_Pet_Sheet_Should_Link_Owner_By_Name_And_Gsm()
    {
        var headers = new[] { "Sahip", "Gsm", "Hasta", "Protokol", "Tür", "Irk", "Cinsiyet", "Doğum Tarihi", "ÇİP NO" };

        var suggestions = ImportCanonicalSchema.Suggest(ImportTable.Pets, headers, ImportSourceProfile.EVet);

        suggestions.Select(s => s.Suggested).Should().Equal(
            "SahipAdSoyad", "SahipTelefon", "HayvanAdi", "HayvanNo", "Tur", "Irk", "Cinsiyet", "DogumTarihi", "Mikrocip");
        ImportCanonicalSchema.MissingRequired(ImportTable.Pets, suggestions.Select(s => s.Suggested!)).Should().BeEmpty();
    }

    [Fact]
    public void General_Synonyms_Should_Still_Apply_For_Every_Profile()
    {
        foreach (var profile in ImportSourceProfiles.All.Select(p => p.Code))
        {
            ImportCanonicalSchema.Suggest(ImportTable.Vaccinations, ["Aşı Uygulama Tarihi", "Uygulanan Aşı"], profile)
                .Select(s => s.Suggested).Should().Equal("UygulamaTarihi", "AsiAdi");
        }
    }

    [Theory]
    [InlineData(null, ImportSourceProfile.Generic, true)]
    [InlineData("", ImportSourceProfile.Generic, true)]
    [InlineData("evet", ImportSourceProfile.EVet, true)]
    [InlineData("Kolayvet", ImportSourceProfile.Kolayvet, true)]
    [InlineData("1", ImportSourceProfile.Generic, false)]
    [InlineData("Vetinity", ImportSourceProfile.Generic, false)]
    public void TryParse_Should_Accept_Only_Names(string? value, ImportSourceProfile expected, bool ok)
    {
        ImportSourceProfiles.TryParse(value, out var profile).Should().Be(ok);
        if (ok)
            profile.Should().Be(expected);
    }

    [Fact]
    public void Batch_Table_Info_Should_Use_Batch_Profile()
    {
        var batch = ImportBatch.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(), DateTime.UtcNow, ImportSourceProfile.EVet).Value!;
        batch.AttachFile(new string('a', 64), 2, 0, 0).IsSuccess.Should().BeTrue();

        var tables = ImportTableInfoLoader.Build(batch, new Dictionary<ImportTable, IReadOnlyList<string>> { [ImportTable.Customers] = ["Adı"] });

        tables.Single().Columns.Single().Should().Be(new ImportColumnDto("Adı", "AdSoyad", ImportSuggestionConfidence.Profile, null));
    }

    [Fact]
    public async Task Source_Profiles_Query_Should_List_Profiles_With_Table_Keys()
    {
        var result = await new GetImportSourceProfilesQueryHandler().Handle(new GetImportSourceProfilesQuery(), CancellationToken.None);

        var evet = result.Value!.Single(p => p.Code == ImportSourceProfile.EVet);
        evet.DisplayName.Should().Be("E-vet");
        evet.Verified.Should().BeFalse();
        evet.Mappings.Should().Contain(new ImportProfileMappingDto("pets", "Hasta Irkı", "Irk", evet.Mappings.First(m => m.Header == "Hasta Irkı").Source, false));
        evet.Mappings.Select(m => m.Table).Distinct().Should().BeEquivalentTo("customers", "pets");
    }
}
