using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Products.Commands.Create;
using Backend.Veteriner.Application.Products.Commands.Create.Validators;
using Backend.Veteriner.Application.Products.Commands.Update;
using Backend.Veteriner.Application.Products.Commands.Update.Validators;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Products.Handlers;

/// <summary>
/// Ürün KDV oranı ve alış fiyatı (BATCH_STOCK_API_CONTRACT.md §2.1): PUT'ta null = dokunma, temizleme bayrakları; kural ihlali
/// <c>Products.Validation</c> (kural domain'de; doğrulayıcı alanı reddetmez, kod sözleşmedeki gibi domain'den gelir).
/// </summary>
public sealed class ProductTaxFieldsTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Product>> _productsRead = new();
    private readonly Mock<IRepository<Product>> _productsWrite = new();
    private readonly Mock<IReadRepository<ProductCategory>> _categoriesRead = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ProductTaxFieldsTests()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);
    }

    private CreateProductCommandHandler CreateHandler()
        => new(_tenantContext.Object, _productsRead.Object, _productsWrite.Object, _categoriesRead.Object, TimeProvider.System);

    private UpdateProductCommandHandler UpdateHandlerFor(Product product)
    {
        _productsWrite.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByIdTrackedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        return new UpdateProductCommandHandler(
            _tenantContext.Object, _productsRead.Object, _productsWrite.Object, _categoriesRead.Object, TimeProvider.System);
    }

    private Product ExistingProduct() => new(_tenantId, "Deneme ürün", "Adet", 100m, "TRY", DateTime.UtcNow, vatRate: 20m, purchasePrice: 55m);

    private static UpdateProductCommand Update(Guid id, decimal? vatRate = null, decimal? purchasePrice = null, bool clearVat = false, bool clearPurchase = false)
        => new(id, null, "Deneme ürün", null, null, null, "Adet", 100m, "TRY",
            VatRate: vatRate, PurchasePrice: purchasePrice, ClearVatRate: clearVat, ClearPurchasePrice: clearPurchase);

    [Fact]
    public async Task Create_Should_Return_Vat_Rate_And_Purchase_Price()
    {
        var result = await CreateHandler().Handle(
            new CreateProductCommand(null, "Deneme ürün", null, null, null, "Adet", 100m, "TRY", VatRate: 10m, PurchasePrice: 62.5m),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.VatRate.Should().Be(10m);
        result.Value.PurchasePrice.Should().Be(62.5m);
    }

    [Theory]
    [InlineData(101d, null)]
    [InlineData(null, -1d)]
    public async Task Create_Should_Reject_Invalid_Values_With_Products_Validation(double? vatRate, double? purchasePrice)
    {
        var result = await CreateHandler().Handle(
            new CreateProductCommand(null, "Deneme ürün", null, null, null, "Adet", 100m, "TRY",
                VatRate: (decimal?)vatRate, PurchasePrice: (decimal?)purchasePrice),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.Validation");
        _productsWrite.Verify(x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Without_Values_Should_Not_Touch_Them()
    {
        var product = ExistingProduct();

        (await UpdateHandlerFor(product).Handle(Update(product.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();

        product.VatRate.Should().Be(20m);
        product.PurchasePrice.Should().Be(55m);
    }

    [Fact]
    public async Task Update_Should_Replace_Values()
    {
        var product = ExistingProduct();

        (await UpdateHandlerFor(product).Handle(Update(product.Id, vatRate: 1m, purchasePrice: 70m), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        product.VatRate.Should().Be(1m);
        product.PurchasePrice.Should().Be(70m);
    }

    [Fact]
    public async Task Update_With_Clear_Flags_Should_Clear_Values()
    {
        var product = ExistingProduct();

        (await UpdateHandlerFor(product).Handle(Update(product.Id, clearVat: true, clearPurchase: true), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        product.VatRate.Should().BeNull();
        product.PurchasePrice.Should().BeNull();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Update_With_Value_And_Clear_Should_Fail(bool vat, bool purchase)
    {
        var product = ExistingProduct();

        var result = await UpdateHandlerFor(product).Handle(
            Update(product.Id, vatRate: vat ? 10m : null, purchasePrice: purchase ? 10m : null, clearVat: vat, clearPurchase: purchase),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.Validation");
        product.VatRate.Should().Be(20m);
        product.PurchasePrice.Should().Be(55m);
    }

    [Fact]
    public async Task Update_Should_Reject_Rate_With_Three_Decimals()
    {
        var product = ExistingProduct();

        var result = await UpdateHandlerFor(product).Handle(Update(product.Id, vatRate: 18.125m), CancellationToken.None);

        result.Error.Code.Should().Be("Products.Validation");
        product.VatRate.Should().Be(20m);
    }

    [Fact]
    public void Validators_Should_Leave_Tax_Fields_To_Domain_Rule()
    {
        new CreateProductCommandValidator()
            .Validate(new CreateProductCommand(null, "Deneme ürün", null, null, null, "Adet", 100m, "TRY", VatRate: 150m, PurchasePrice: -1m))
            .IsValid.Should().BeTrue();
        new UpdateProductCommandValidator()
            .Validate(Update(Guid.NewGuid(), vatRate: 150m, clearVat: true))
            .IsValid.Should().BeTrue();
    }
}
