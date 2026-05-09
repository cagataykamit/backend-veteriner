using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.Products.Commands.Activate;
using Backend.Veteriner.Application.Products.Commands.Create;
using Backend.Veteriner.Application.Products.Commands.Deactivate;
using Backend.Veteriner.Application.Products.Commands.Update;
using Backend.Veteriner.Application.Products.Queries.GetById;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Products.Handlers;

public sealed class ProductHandlersTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Product>> _productsRead = new();
    private readonly Mock<IRepository<Product>> _productsWrite = new();
    private readonly Mock<IReadRepository<ProductCategory>> _categoriesRead = new();

    [Fact]
    public async Task Create_Should_Succeed()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _productsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByTenantAndSkuSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var handler = new CreateProductCommandHandler(
            _tenantContext.Object,
            _productsRead.Object,
            _productsWrite.Object,
            _categoriesRead.Object);

        var result = await handler.Handle(
            new CreateProductCommand(null, "Mama", "SKU-1", null, null, "Adet", 10, "TRY"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _productsWrite.Verify(x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateSku_Should_Fail()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _productsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByTenantAndSkuSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product(tenantId, "Var", "Adet", 1, "TRY", sku: "sku-1"));

        var handler = new CreateProductCommandHandler(
            _tenantContext.Object,
            _productsRead.Object,
            _productsWrite.Object,
            _categoriesRead.Object);

        var result = await handler.Handle(
            new CreateProductCommand(null, "Yeni", "SKU-1", null, null, "Adet", 10, "TRY"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.SkuAlreadyExists");
    }

    [Fact]
    public async Task Create_InvalidCategory_Should_Fail()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _categoriesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductCategoryByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCategory?)null);

        var handler = new CreateProductCommandHandler(
            _tenantContext.Object,
            _productsRead.Object,
            _productsWrite.Object,
            _categoriesRead.Object);

        var result = await handler.Handle(
            new CreateProductCommand(Guid.NewGuid(), "Ürün", null, null, null, "Adet", 10, "TRY"),
            CancellationToken.None);

        result.Error.Code.Should().Be("Products.CategoryNotFound");
    }

    [Fact]
    public async Task Create_InactiveCategory_Should_Fail()
    {
        var tenantId = Guid.NewGuid();
        var category = new ProductCategory(tenantId, "K");
        category.Deactivate();

        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _categoriesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductCategoryByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var handler = new CreateProductCommandHandler(
            _tenantContext.Object,
            _productsRead.Object,
            _productsWrite.Object,
            _categoriesRead.Object);

        var result = await handler.Handle(
            new CreateProductCommand(category.Id, "Ürün", null, null, null, "Adet", 10, "TRY"),
            CancellationToken.None);

        result.Error.Code.Should().Be("Products.CategoryInactive");
    }

    [Fact]
    public async Task Update_Should_Succeed()
    {
        var tenantId = Guid.NewGuid();
        var product = new Product(tenantId, "Eski", "Adet", 1, "TRY", sku: "a");
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _productsWrite.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByIdTrackedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _productsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByTenantAndSkuSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var handler = new UpdateProductCommandHandler(
            _tenantContext.Object,
            _productsRead.Object,
            _productsWrite.Object,
            _categoriesRead.Object);

        var result = await handler.Handle(
            new UpdateProductCommand(product.Id, null, "Yeni", "b", null, null, "Kutu", 20, "TRY"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Name.Should().Be("Yeni");
    }

    [Fact]
    public async Task GetById_NotFound_Should_Fail()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _productsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var handler = new GetProductByIdQueryHandler(_tenantContext.Object, _productsRead.Object, _categoriesRead.Object);
        var result = await handler.Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.NotFound");
    }

    [Fact]
    public async Task Deactivate_Then_Activate_Should_Succeed()
    {
        var tenantId = Guid.NewGuid();
        var product = new Product(tenantId, "U", "Adet", 10, "TRY");
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _productsWrite.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByIdTrackedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var deact = new DeactivateProductCommandHandler(_tenantContext.Object, _productsWrite.Object);
        var act = new ActivateProductCommandHandler(_tenantContext.Object, _productsWrite.Object);

        var d = await deact.Handle(new DeactivateProductCommand(product.Id), CancellationToken.None);
        var a = await act.Handle(new ActivateProductCommand(product.Id), CancellationToken.None);

        d.IsSuccess.Should().BeTrue();
        a.IsSuccess.Should().BeTrue();
    }
}
