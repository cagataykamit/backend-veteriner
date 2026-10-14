using Backend.Veteriner.Application.Imports.Mapping;
using FluentAssertions;

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
