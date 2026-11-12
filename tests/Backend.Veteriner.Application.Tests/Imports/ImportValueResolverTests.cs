using Backend.Veteriner.Application.Imports.Mapping;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

public sealed class ImportValueResolverTests
{
    private static readonly Guid Dog = Guid.NewGuid();
    private static readonly Guid Cat = Guid.NewGuid();
    private static readonly Guid Bird = Guid.NewGuid();
    private static readonly Guid GoldenRetriever = Guid.NewGuid();
    private static readonly Guid Tekir = Guid.NewGuid();
    private static readonly Guid Yellow = Guid.NewGuid();
    private static readonly Guid Rabies = Guid.NewGuid();
    private static readonly Guid Mixed = Guid.NewGuid();
    private static readonly Guid FeLv = Guid.NewGuid();
    private static readonly Guid Bronchine = Guid.NewGuid();

    private static readonly ImportCatalogs Catalogs = new(
        Species:
        [
            new(Dog, "Köpek", "DOG", null),
            new(Cat, "Kedi", "CAT", null),
            new(Bird, "Kuş", "BIRD", null),
        ],
        Breeds:
        [
            new(GoldenRetriever, "Golden Retriever", null, Dog),
            new(Tekir, "Tekir", null, Cat),
        ],
        Colors: [new(Yellow, "Sarı", "YELLOW", null)],
        Vaccines:
        [
            new(Rabies, "Kuduz", "RABIES", null),
            new(Mixed, "Karma", "MIXED", null),
            new(FeLv, "Lösemi", "LEUKEMIA", Cat),
            new(Bronchine, "Bronşin", "BRONCHINE", Dog),
        ]);

    private static ImportValueResolver Resolver(params ImportValueMapping[] userMappings) => new(Catalogs, userMappings);

    // E-vet geçişinde görülen adlar için genişletilmiş katalog (sistem kataloğunun 2026-11-12 eklemeleri).
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Exotic = Guid.NewGuid();
    private static readonly Guid GuineaPig = Guid.NewGuid();
    private static readonly Guid ScottishFold = Guid.NewGuid();
    private static readonly Guid InternalExternal = Guid.NewGuid();
    private static readonly Guid External = Guid.NewGuid();
    private static readonly Guid Internal = Guid.NewGuid();
    private static readonly Guid Fungal = Guid.NewGuid();
    private static readonly Guid MixedRabies = Guid.NewGuid();
    private static readonly Guid DistemperParvo = Guid.NewGuid();
    private static readonly Guid Tetanus = Guid.NewGuid();
    private static readonly Guid Mastitis = Guid.NewGuid();
    private static readonly Guid Enterotoxemia = Guid.NewGuid();
    private static readonly Guid Setter = Guid.NewGuid();
    private static readonly Guid Malinois = Guid.NewGuid();
    private static readonly Guid Calico = Guid.NewGuid();
    private static readonly Guid BrownWhite = Guid.NewGuid();
    private static readonly Guid YellowWhite = Guid.NewGuid();
    private static readonly Guid LightBrown = Guid.NewGuid();

    private static readonly ImportValueResolver Extended = new(new ImportCatalogs(
        Species: [new(Dog, "Köpek", "DOG", null), new(Cat, "Kedi", "CAT", null), new(GuineaPig, "Kobay (Gine Domuzu)", "GUINEA_PIG", null), new(Exotic, "Egzotik", "EXOTIC", null), new(Other, "Diğer", "OTHER", null)],
        Breeds: [new(ScottishFold, "Scottish Fold", null, Cat), new(Setter, "İngiliz Setter", null, Dog), new(Malinois, "Belçika Malinois", null, Dog)],
        Colors:
        [
            new(Calico, "Alacalı", "CALICO", null),
            new(BrownWhite, "Kahverengi-Beyaz", "BROWN_WHITE", null),
            new(YellowWhite, "Sarı-Beyaz", "YELLOW_WHITE", null),
            new(LightBrown, "Açık Kahverengi", "LIGHT_BROWN", null),
        ],
        Vaccines:
        [
            new(Mixed, "Karma", "MIXED", null),
            new(InternalExternal, "İç-Dış Parazit", "INTERNAL_EXTERNAL_PARASITE", null),
            new(External, "Dış Parazit", "EXTERNAL_PARASITE", null),
            new(Internal, "İç Parazit", "INTERNAL_PARASITE", null),
            new(Fungal, "Mantar", "DERMATOPHYTOSIS", null),
            new(MixedRabies, "Karma + Kuduz", "MIXED_RABIES", null),
            new(DistemperParvo, "DP (Distemper-Parvo)", "DISTEMPER_PARVO", null),
            new(Tetanus, "Tetanoz", "TETANUS", null),
            new(Mastitis, "Mastitis", "MASTITIS", null),
            new(Enterotoxemia, "Enterotoksemi", "ENTEROTOXEMIA", null),
        ]), []);

    public static TheoryData<string, string> EVetVaccineNames => new()
    {
        { "İÇ+DIŞ PARAZİT", nameof(InternalExternal) },
        { "İç-Dış Parazit 3 Aylık", nameof(InternalExternal) },
        { "Nexgard Spectra", nameof(InternalExternal) },
        { "Nexgard Combo", nameof(InternalExternal) },
        { "BRAVECTO", nameof(External) },
        { "Seresto", nameof(External) },
        { "SCALİBOR", nameof(External) },
        { "Nexgard", nameof(External) },
        { "Drontal", nameof(Internal) },
        { "MANTAR", nameof(Fungal) },
        { "MANTAR 2", nameof(Fungal) },
        { "Kedi Karma + Kuduz", nameof(MixedRabies) },
        { "Köpek Karma +Kuduz", nameof(MixedRabies) },
        { "PUPPY DP", nameof(DistemperParvo) },
        { "TETANOZ YILLIK", nameof(Tetanus) },
        { "TETANOZ 1", nameof(Tetanus) },
        { "Mastitis Aşısı", nameof(Mastitis) },
        { "Mera Aşısı", nameof(Enterotoxemia) },
    };

    [Theory]
    [MemberData(nameof(EVetVaccineNames))]
    public void EVet_Vaccine_And_Brand_Names_Should_Resolve_To_The_System_Catalog(string source, string expected)
        => Extended.Resolve(ImportValueKind.Vaccine, source, Dog).Should().Be(ImportValueResolution.Resolved(ExtendedVaccines[expected]));

    /// <summary>Kimlikler çalıştırmada üretilir; teori verisi adla taşır (keşif ve çalıştırma ayrı değer görür).</summary>
    private static readonly Dictionary<string, Guid> ExtendedVaccines = new()
    {
        [nameof(InternalExternal)] = InternalExternal,
        [nameof(External)] = External,
        [nameof(Internal)] = Internal,
        [nameof(Fungal)] = Fungal,
        [nameof(MixedRabies)] = MixedRabies,
        [nameof(DistemperParvo)] = DistemperParvo,
        [nameof(Tetanus)] = Tetanus,
        [nameof(Mastitis)] = Mastitis,
        [nameof(Enterotoxemia)] = Enterotoxemia,
    };

    [Theory]
    [InlineData("UYUZ TEDAVİSİ")]
    [InlineData("ZYLEXİS")]
    public void Treatments_Should_Stay_Unmapped(string source)
        => Extended.Resolve(ImportValueKind.Vaccine, source, Dog).Kind.Should().Be(ImportValueResolutionKind.Unmapped);

    [Fact]
    public void Exotic_And_Common_Species_Words_Should_Resolve()
    {
        Extended.Resolve(ImportValueKind.Species, "Egzotik").TargetId.Should().Be(Exotic);
        Extended.Resolve(ImportValueKind.Species, "Exotic").TargetId.Should().Be(Exotic);
        Extended.Resolve(ImportValueKind.Species, "Kobay").TargetId.Should().Be(GuineaPig);
        Extended.Resolve(ImportValueKind.Species, "Gine Domuzu").TargetId.Should().Be(GuineaPig);
        Extended.Resolve(ImportValueKind.Species, "Guinea Pig").TargetId.Should().Be(GuineaPig);
        Extended.Resolve(ImportValueKind.Species, "ARABA").Kind.Should().Be(ImportValueResolutionKind.Unmapped);
    }

    [Fact]
    public void Breed_Misspellings_Should_Resolve_Within_Species()
    {
        Extended.Resolve(ImportValueKind.Breed, "Scottisch Fold", Cat).TargetId.Should().Be(ScottishFold);
        Extended.Resolve(ImportValueKind.Breed, "Scottisch Fold", Dog).Kind.Should().Be(ImportValueResolutionKind.Unmapped);
        Extended.Resolve(ImportValueKind.Breed, "Setter", Dog).TargetId.Should().Be(Setter);
        Extended.Resolve(ImportValueKind.Breed, "Belçika Kurdu", Dog).TargetId.Should().Be(Malinois);
        Extended.Resolve(ImportValueKind.Breed, "Terrier", Dog).Kind.Should().Be(ImportValueResolutionKind.Unmapped);
    }

    [Theory]
    [InlineData("Üç Renkli", "CALICO")]
    [InlineData("Kahve Beyaz", "BROWN_WHITE")]
    [InlineData("Beyaz - Sarı", "YELLOW_WHITE")]
    [InlineData("Sarı Beyaz", "YELLOW_WHITE")]
    [InlineData("Açık Kahve", "LIGHT_BROWN")]
    public void EVet_Color_Spellings_Should_Resolve(string source, string code)
        => Extended.Resolve(ImportValueKind.Color, source).TargetId.Should().Be(ExtendedColors[code]);

    private static readonly Dictionary<string, Guid> ExtendedColors = new()
    {
        ["CALICO"] = Calico,
        ["BROWN_WHITE"] = BrownWhite,
        ["YELLOW_WHITE"] = YellowWhite,
        ["LIGHT_BROWN"] = LightBrown,
    };

    [Theory]
    [InlineData("YILLIK KEDİ KARMA", "karma")]
    [InlineData("KÖPEK KARMA 3", "karma")]
    [InlineData("LÖSEMİ  1", "losemi")]
    [InlineData("İÇ PARAZİT 2 AYLIK", "icparazit")]
    [InlineData("İç Parazit 1,5 Aylık", "icparazit")]
    [InlineData("TETANOZ YILLIK", "tetanoz")]
    [InlineData("Kedi", "kedi")]
    [InlineData("İÇ+DIŞ PARAZİT", "icdisparazit")]
    public void Vaccine_Series_Key_Should_Drop_Dose_Year_And_Species_Words(string name, string expected)
        => ImportValueResolver.VaccineSeriesKey(name).Should().Be(expected);

    [Theory]
    [InlineData("YILLIK KEDİ KARMA")]
    [InlineData("KÖPEK KARMA 2")]
    [InlineData("Karma Aşısı")]
    public void Vaccine_With_Dose_Or_Year_Words_Should_Resolve_To_Its_Series(string source)
        => Resolver().Resolve(ImportValueKind.Vaccine, source, Dog).Should().Be(ImportValueResolution.Resolved(Mixed));

    [Fact]
    public void Exact_Vaccine_Name_Should_Win_Over_Series_Reduction()
    {
        var yearly = Guid.NewGuid();
        var catalogs = Catalogs with { Vaccines = [.. Catalogs.Vaccines, new(yearly, "Yıllık Karma", null, null)] };

        new ImportValueResolver(catalogs, []).Resolve(ImportValueKind.Vaccine, "YILLIK KARMA", Dog)
            .Should().Be(ImportValueResolution.Resolved(yearly));
    }

    [Theory]
    [InlineData("Köpek")]
    [InlineData("KÖPEK")]
    [InlineData("kopek")]
    [InlineData("  Kopek ")]
    [InlineData("dog")]
    [InlineData("DOG")]
    public void Species_Should_Match_By_Folded_Name_Code_Or_Alias(string source)
    {
        var result = Resolver().Resolve(ImportValueKind.Species, source);

        result.Kind.Should().Be(ImportValueResolutionKind.Resolved);
        result.TargetId.Should().Be(Dog);
    }

    [Fact]
    public void Dotted_And_Dotless_I_Should_Fold_Together()
        => Resolver().Resolve(ImportValueKind.Species, "KEDİ").TargetId.Should().Be(Cat);

    [Theory]
    [InlineData("Kus", true)]
    [InlineData("Hamster", false)]
    [InlineData("Köpek ırkı", false)]
    public void Species_Without_Catalog_Entry_Should_Be_Unmapped_And_Never_Created(string source, bool resolved)
    {
        var result = Resolver().Resolve(ImportValueKind.Species, source);

        (result.Kind == ImportValueResolutionKind.Resolved).Should().Be(resolved);
        if (!resolved)
            result.Should().Be(ImportValueResolution.Unmapped);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Blank_Source_Should_Be_Empty(string? source)
        => Resolver().Resolve(ImportValueKind.Species, source).Should().Be(ImportValueResolution.Empty);

    [Fact]
    public void User_Mapping_Should_Win_Over_Catalog_And_Aliases()
    {
        var resolver = Resolver(new ImportValueMapping("species", "KOPEK", Bird));

        resolver.Resolve(ImportValueKind.Species, "Köpek").TargetId.Should().Be(Bird);
    }

    [Fact]
    public void User_Mapping_Should_Resolve_Free_Text_And_Null_Target_Should_Skip()
    {
        var resolver = Resolver(
            new ImportValueMapping("species", "Muhabbet kuşu", Bird),
            new ImportValueMapping("vaccine", "Kuduz-2023", Rabies),
            new ImportValueMapping("vaccine", "Bilinmeyen aşı", null));

        resolver.Resolve(ImportValueKind.Species, "muhabbet KUŞU").TargetId.Should().Be(Bird);
        resolver.Resolve(ImportValueKind.Vaccine, "kuduz_2023").TargetId.Should().Be(Rabies);
        resolver.Resolve(ImportValueKind.Vaccine, "Bilinmeyen Aşı").Should().Be(ImportValueResolution.Skipped);
    }

    [Fact]
    public void User_Mapping_Should_Be_Scoped_By_Kind()
    {
        var resolver = Resolver(new ImportValueMapping("color", "Kuş", Yellow));

        resolver.Resolve(ImportValueKind.Species, "Kuş").TargetId.Should().Be(Bird);
        resolver.Resolve(ImportValueKind.Color, "Kuş").TargetId.Should().Be(Yellow);
    }

    [Fact]
    public void Unknown_User_Mapping_Kind_Should_Be_Ignored()
        => Resolver(new ImportValueMapping("zzz", "Hamster", Bird)).Resolve(ImportValueKind.Species, "Hamster")
            .Should().Be(ImportValueResolution.Unmapped);

    [Fact]
    public void Breed_Should_Resolve_Only_Within_The_Pets_Species()
    {
        var resolver = Resolver();

        resolver.Resolve(ImportValueKind.Breed, "golden retriever", Dog).TargetId.Should().Be(GoldenRetriever);
        resolver.Resolve(ImportValueKind.Breed, "golden retriever", Cat).Should().Be(ImportValueResolution.Unmapped);
        resolver.Resolve(ImportValueKind.Breed, "Tekir", null).Should().Be(ImportValueResolution.Unmapped);
    }

    [Fact]
    public void Vaccine_Should_Match_Name_Code_And_Builtin_Aliases()
    {
        var resolver = Resolver();

        resolver.Resolve(ImportValueKind.Vaccine, "Kuduz", Dog).TargetId.Should().Be(Rabies);
        resolver.Resolve(ImportValueKind.Vaccine, "rabies", Dog).TargetId.Should().Be(Rabies);
        resolver.Resolve(ImportValueKind.Vaccine, "Kuduz aşısı", Dog).TargetId.Should().Be(Rabies);
        resolver.Resolve(ImportValueKind.Vaccine, "Karma Aşısı", Cat).TargetId.Should().Be(Mixed);
        resolver.Resolve(ImportValueKind.Vaccine, "Bronchine", Dog).TargetId.Should().Be(Bronchine);
    }

    [Fact]
    public void Vaccine_Definition_With_Species_Should_Not_Match_Other_Species()
    {
        var resolver = Resolver();

        resolver.Resolve(ImportValueKind.Vaccine, "Lösemi", Cat).TargetId.Should().Be(FeLv);
        resolver.Resolve(ImportValueKind.Vaccine, "losemi", Dog).Should().Be(ImportValueResolution.Unmapped);
        // Hastanın türü bilinmiyorsa tür süzmesi yapılmaz.
        resolver.Resolve(ImportValueKind.Vaccine, "Lösemi").TargetId.Should().Be(FeLv);
    }

    [Fact]
    public void Color_Should_Match_By_Name_And_Code()
    {
        var resolver = Resolver();

        resolver.Resolve(ImportValueKind.Color, "sari").TargetId.Should().Be(Yellow);
        resolver.Resolve(ImportValueKind.Color, "YELLOW").TargetId.Should().Be(Yellow);
        resolver.Resolve(ImportValueKind.Color, "Mor").Should().Be(ImportValueResolution.Unmapped);
    }

    [Fact]
    public void Ambiguous_Catalog_Match_Should_Not_Be_Resolved_Automatically()
    {
        var other = Guid.NewGuid();
        var catalogs = Catalogs with
        {
            Species = [.. Catalogs.Species, new(other, "KOPEK", "DOG2", null)],
        };

        new ImportValueResolver(catalogs, []).Resolve(ImportValueKind.Species, "kopek")
            .Should().Be(ImportValueResolution.Unmapped);
    }

    [Fact]
    public void Alias_Should_Not_Resolve_When_Catalog_Has_No_Target()
    {
        var catalogs = Catalogs with { Species = [new(Cat, "Kedi", "CAT", null)] };

        new ImportValueResolver(catalogs, []).Resolve(ImportValueKind.Species, "dog")
            .Should().Be(ImportValueResolution.Unmapped);
    }
}
