using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.DrugGuide;

/// <summary>REF-001 Dilim 1–2: ruhsatlı ürün kaydı, hedef tür kuralı ve ürün kartı bağı.</summary>
public sealed class LicensedProductTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Cat = Guid.NewGuid();
    private static readonly Guid Dog = Guid.NewGuid();

    private static LicensedProduct Product(
        IReadOnlyList<LicensedProductTargetSpeciesDraft>? species = null,
        IReadOnlyList<Guid>? ingredients = null,
        string? spcUrl = null)
        => new(" Örnek Tablet ", " Örnek İlaç A.Ş. ", " R-1 ", spcUrl, "HBS", ingredients ?? [Guid.NewGuid()], species ?? [], Now);

    [Fact]
    public void Ctor_Should_Trim_Normalize_And_Keep_Ingredient_Order()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var p = Product(ingredients: [first, second]);

        p.Name.Should().Be("Örnek Tablet");
        p.NormalizedName.Should().Be("ornek tablet");
        p.NormalizedLicenseHolder.Should().Be("ornek ilac a.s.");
        p.LicenseNumber.Should().Be("R-1");
        p.IsListedInSource.Should().BeTrue();
        p.Ingredients.Select(i => (i.ActiveIngredientId, i.SortOrder)).Should().Equal((first, 1), (second, 2));
    }

    [Fact]
    public void Ctor_Should_Reject_Missing_Or_Duplicate_Ingredients_And_Non_Https_Link()
    {
        var id = Guid.NewGuid();

        ((Action)(() => Product(ingredients: []))).Should().Throw<ArgumentException>();
        ((Action)(() => Product(ingredients: [id, id]))).Should().Throw<ArgumentException>();
        ((Action)(() => Product(spcUrl: "http://ornek.gov.tr/belge.pdf"))).Should().Throw<ArgumentException>();
        Product(spcUrl: "https://ornek.gov.tr/belge.pdf").SpcUrl.Should().Be("https://ornek.gov.tr/belge.pdf");
    }

    [Fact]
    public void IsLicensedFor_Should_Compare_Mapped_Target_Species()
    {
        var p = Product([new("Köpek", Dog), new("Arı", null)]);

        p.IsLicensedFor(Dog).Should().BeTrue();
        p.IsLicensedFor(Cat).Should().BeFalse();
    }

    [Fact]
    public void IsLicensedFor_Should_Be_Unknown_When_No_Target_Species_Is_Mapped()
    {
        Product([new("Arı", null)]).IsLicensedFor(Cat).Should().BeNull();
        Product().IsLicensedFor(Cat).Should().BeNull();
    }

    [Fact]
    public void MarkDelistedFromSource_Should_Keep_Record_And_Flag_It()
    {
        var p = Product();

        p.MarkDelistedFromSource(Now.AddDays(30));

        p.IsListedInSource.Should().BeFalse();
        p.SourceSyncedAtUtc.Should().Be(Now.AddDays(30));
    }

    [Fact]
    public void ActiveIngredient_Should_Map_Spelling_Variants_To_Same_Key()
    {
        new ActiveIngredient("İvermektin").NormalizedName.Should().Be(new ActiveIngredient(" ivermektin ").NormalizedName);
    }

    [Fact]
    public void Product_Card_Link_Should_Be_Set_And_Cleared()
    {
        var card = new Product(Guid.NewGuid(), "Klinik ürünü", "Kutu", 10m, "TRY", Now);
        var licensedId = Guid.NewGuid();

        card.LinkLicensedProduct(licensedId, Now);
        card.LicensedProductId.Should().Be(licensedId);

        card.LinkLicensedProduct(null, Now);
        card.LicensedProductId.Should().BeNull();

        ((Action)(() => card.LinkLicensedProduct(Guid.Empty, Now))).Should().Throw<ArgumentException>();
    }
}
