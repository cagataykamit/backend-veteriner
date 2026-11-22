using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Products;

/// <summary>Kayıtlı adın yazım onarımı (DbMigrator <c>fix-product-names</c>) yalnız adı değiştirir.</summary>
public sealed class ProductRenameTests
{
    private static readonly DateTime Created = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Rename_Should_Change_Only_Name_And_Stamp_Update()
    {
        var product = new Product(Guid.NewGuid(), "Flexı Mavı", "Adet", 450m, "TRY", Created, barcode: "8690000000001", vatRate: 20m);

        product.Rename(" Flexi Mavi ", Now);

        product.Name.Should().Be("Flexi Mavi");
        product.UnitPrice.Should().Be(450m);
        product.Barcode.Should().Be("8690000000001");
        product.VatRate.Should().Be(20m);
        product.CreatedAtUtc.Should().Be(Created);
        product.UpdatedAtUtc.Should().Be(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_Should_Reject_Blank_Name(string name)
    {
        var product = new Product(Guid.NewGuid(), "Mama", "Adet", 10m, "TRY", Created);

        var act = () => product.Rename(name, Now);

        act.Should().Throw<ArgumentException>();
        product.Name.Should().Be("Mama");
    }
}
