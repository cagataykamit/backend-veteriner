using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Products;

/// <summary>TIER-006: kayıt, benzersizlik denetimi ve barkodla arama aynı biçimi kullanır.</summary>
public sealed class ProductBarcodeTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" 8690000000001 ", "8690000000001")]
    [InlineData("ABC-12", "ABC-12")]
    public void NormalizeBarcode_Should_Trim_And_Treat_Blank_As_Empty(string? input, string? expected)
        => Product.NormalizeBarcode(input).Should().Be(expected);

    [Fact]
    public void Create_And_Update_Should_Store_Normalized_Barcode()
    {
        var product = new Product(TenantId, "Mama", "Adet", 10m, "TRY", Now, barcode: " 8690000000001 ");
        product.Barcode.Should().Be("8690000000001");

        product.Update(null, "Mama", "Adet", 10m, "TRY", Now, barcode: "  ");
        product.Barcode.Should().BeNull();
    }
}
