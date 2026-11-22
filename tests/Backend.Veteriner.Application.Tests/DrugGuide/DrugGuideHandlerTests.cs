using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DrugGuide.Queries.GetProductDrugInfo;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Products.Commands.LinkLicensedProduct;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.DrugGuide;

/// <summary>REF-001 Dilim 2: reçete/uygulama satırı rehber bilgisi ve ürün kartı bağı.</summary>
public sealed class DrugGuideHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Product>> _products = new();
    private readonly Mock<IRepository<Product>> _productsWrite = new();
    private readonly Mock<IReadRepository<LicensedProduct>> _licensed = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();

    public DrugGuideHandlerTests() => _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);

    private static LicensedProduct Licensed(params (string Label, Guid? SpeciesId)[] species)
        => new("Örnek Tablet", "Örnek A.Ş.", null, null, "HBS", [Guid.NewGuid()], species.Select(s => new LicensedProductTargetSpeciesDraft(s.Label, s.SpeciesId)).ToList(), Now);

    private Product Card(Guid? licensedProductId)
    {
        var p = new Product(_tenantId, "Klinik ürünü", "Kutu", 10m, "TRY", Now);
        if (licensedProductId is { } id)
            p.LinkLicensedProduct(id, Now);
        return p;
    }

    private Pet PetOf(Guid speciesId) => new(_tenantId, Guid.NewGuid(), "Pamuk", speciesId, null, null);

    private GetProductDrugInfoQueryHandler InfoHandler() => new(_tenantContext.Object, _products.Object, _licensed.Object, _pets.Object);

    [Fact]
    public async Task ProductInfo_Should_Flag_When_Pet_Species_Not_In_License_And_Skip_Unlinked_Products()
    {
        var dog = Guid.NewGuid();
        var cat = Guid.NewGuid();
        var licensed = Licensed(("Köpek", dog));
        var linked = Card(licensed.Id);
        var unlinked = Card(null);
        _products.Setup(x => x.ListAsync(It.IsAny<ProductsByIdsSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync([linked, unlinked]);
        _licensed.Setup(x => x.ListAsync(It.IsAny<LicensedProductsByIdsSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync([licensed]);
        _pets.Setup(x => x.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(PetOf(cat));

        var result = await InfoHandler().Handle(new GetProductDrugInfoQuery([linked.Id, unlinked.Id], Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var info = result.Value!.Should().ContainSingle().Subject;
        info.ProductId.Should().Be(linked.Id);
        info.TargetSpecies.Should().Equal("Köpek");
        info.NotLicensedForPetSpecies.Should().BeTrue();
    }

    [Fact]
    public async Task ProductInfo_Should_Not_Flag_Without_Pet_Or_When_Species_Matches()
    {
        var dog = Guid.NewGuid();
        var licensed = Licensed(("Köpek", dog));
        var linked = Card(licensed.Id);
        _products.Setup(x => x.ListAsync(It.IsAny<ProductsByIdsSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync([linked]);
        _licensed.Setup(x => x.ListAsync(It.IsAny<LicensedProductsByIdsSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync([licensed]);
        _pets.Setup(x => x.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(PetOf(dog));

        (await InfoHandler().Handle(new GetProductDrugInfoQuery([linked.Id]), CancellationToken.None)).Value!.Single().NotLicensedForPetSpecies.Should().BeNull();
        (await InfoHandler().Handle(new GetProductDrugInfoQuery([linked.Id], Guid.NewGuid()), CancellationToken.None)).Value!.Single().NotLicensedForPetSpecies.Should().BeFalse();
    }

    [Fact]
    public async Task Link_Should_Reject_Unknown_Licensed_Product_And_Save_Valid_Link()
    {
        var card = Card(null);
        var licensed = Licensed();
        _productsWrite.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByIdTrackedSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(card);
        _licensed.Setup(x => x.FirstOrDefaultAsync(It.IsAny<LicensedProductsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LicensedProductsByIdsSpec _, CancellationToken _) => null);
        var handler = new LinkLicensedProductCommandHandler(_tenantContext.Object, _productsWrite.Object, _licensed.Object, TimeProvider.System);

        var unknown = await handler.Handle(new LinkLicensedProductCommand(card.Id, Guid.NewGuid()), CancellationToken.None);
        unknown.Error.Code.Should().Be("DrugGuide.ProductNotFound");
        card.LicensedProductId.Should().BeNull();

        _licensed.Setup(x => x.FirstOrDefaultAsync(It.IsAny<LicensedProductsByIdsSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(licensed);
        (await handler.Handle(new LinkLicensedProductCommand(card.Id, licensed.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        card.LicensedProductId.Should().Be(licensed.Id);
        _productsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
