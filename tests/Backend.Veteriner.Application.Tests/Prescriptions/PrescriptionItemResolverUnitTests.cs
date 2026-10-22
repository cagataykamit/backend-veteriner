using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Application.Prescriptions.Items;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Prescriptions;

/// <summary>ADR-036 Dilim 2: reçete kaleminde birim boşsa katalog ürününün stok birimi kopyalanır; doz/parti olduğu gibi geçer.</summary>
public sealed class PrescriptionItemResolverUnitTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<IReadRepository<Product>> _products = new();

    private Product SetupProduct()
    {
        var product = new Product(_tenantId, "Amoksisilin", "Kutu", 10m, "TRY");
        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ProductByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(product);
        return product;
    }

    [Fact]
    public async Task Empty_Unit_Should_Default_To_Product_Unit()
    {
        var product = SetupProduct();

        var result = await PrescriptionItemResolver.ResolveAsync(
            _products.Object, _tenantId, [new PrescriptionItemRequest(null, product.Id, null, 2m, "1 tablet", null, "LOT-1")], CancellationToken.None);

        var draft = result.Value!.Single();
        draft.Unit.Should().Be("Kutu");
        draft.Dose.Should().Be("1 tablet");
        draft.LotNumber.Should().Be("LOT-1");
    }

    [Fact]
    public async Task Given_Unit_Should_Be_Kept_And_Free_Item_Should_Have_No_Default()
    {
        var product = SetupProduct();

        var result = await PrescriptionItemResolver.ResolveAsync(
            _products.Object,
            _tenantId,
            [new PrescriptionItemRequest(null, product.Id, null, 1m, null, "şişe"), new PrescriptionItemRequest("Dış ilaç", null, null, 1m)],
            CancellationToken.None);

        result.Value!.Select(d => d.Unit).Should().Equal("şişe", null);
    }
}
