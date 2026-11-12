using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Products;

/// <summary>Ürün KDV oranı ve alış fiyatı (BATCH_STOCK_API_CONTRACT.md §2.1).</summary>
public sealed class ProductTaxAndPurchasePriceTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void New_Product_Should_Have_No_Vat_Rate_Or_Purchase_Price_By_Default()
    {
        var product = new Product(TenantId, "Kalem", "Adet", 100m, "TRY", Now);

        product.VatRate.Should().BeNull();
        product.PurchasePrice.Should().BeNull();
    }

    [Fact]
    public void Constructor_Should_Store_Vat_Rate_And_Purchase_Price()
    {
        var product = new Product(TenantId, "Kalem", "Adet", 100m, "TRY", Now, vatRate: 10m, purchasePrice: 62.5m);

        product.VatRate.Should().Be(10m);
        product.PurchasePrice.Should().Be(62.5m);
        product.UpdatedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(18.5)]
    [InlineData(100)]
    public void Validate_Should_Accept_Rates_In_Range(double rate)
        => Product.ValidateTaxAndPurchasePrice((decimal)rate, null).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData(-1d, null)]
    [InlineData(100.01, null)]
    [InlineData(18.555, null)]
    [InlineData(null, -0.01)]
    [InlineData(null, 10.005)]
    public void Validate_Should_Reject_Invalid_Rate_Or_Price(double? rate, double? price)
    {
        var result = Product.ValidateTaxAndPurchasePrice((decimal?)rate, (decimal?)price);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.Validation");
    }

    [Fact]
    public void Constructor_Should_Throw_For_Invalid_Vat_Rate()
    {
        var create = () => new Product(TenantId, "Kalem", "Adet", 100m, "TRY", Now, vatRate: 120m);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_Should_Replace_And_Clear_Values()
    {
        var product = new Product(TenantId, "Kalem", "Adet", 100m, "TRY", Now, vatRate: 20m, purchasePrice: 40m);

        product.Update(null, "Kalem", "Adet", 100m, "TRY", Now, vatRate: 10m, purchasePrice: null);

        product.VatRate.Should().Be(10m);
        product.PurchasePrice.Should().BeNull();
    }

    [Fact]
    public void Update_Should_Throw_For_Negative_Purchase_Price()
    {
        var product = new Product(TenantId, "Kalem", "Adet", 100m, "TRY", Now);

        var update = () => product.Update(null, "Kalem", "Adet", 100m, "TRY", Now, purchasePrice: -5m);

        update.Should().Throw<ArgumentException>();
    }
}
