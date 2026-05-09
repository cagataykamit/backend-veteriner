using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Commands.Activate;
using Backend.Veteriner.Application.ProductCategories.Commands.Create;
using Backend.Veteriner.Application.ProductCategories.Commands.Deactivate;
using Backend.Veteriner.Application.ProductCategories.Commands.Update;
using Backend.Veteriner.Application.ProductCategories.Queries.GetById;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.ProductCategories.Handlers;

public sealed class ProductCategoryHandlersTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<ProductCategory>> _read = new();
    private readonly Mock<IRepository<ProductCategory>> _write = new();

    [Fact]
    public async Task Create_Should_Succeed()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductCategoryByTenantAndNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCategory?)null);

        var handler = new CreateProductCategoryCommandHandler(_tenantContext.Object, _read.Object, _write.Object);
        var result = await handler.Handle(new CreateProductCategoryCommand("İlaç", "Açıklama"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _write.Verify(x => x.AddAsync(It.IsAny<ProductCategory>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateName_Should_Fail()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductCategoryByTenantAndNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductCategory(tenantId, "İlaç"));

        var handler = new CreateProductCategoryCommandHandler(_tenantContext.Object, _read.Object, _write.Object);
        var result = await handler.Handle(new CreateProductCategoryCommand("İlaç"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductCategories.NameAlreadyExists");
    }

    [Fact]
    public async Task Update_Should_Succeed()
    {
        var tenantId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var existing = new ProductCategory(tenantId, "Eski");
        typeof(ProductCategory).GetProperty(nameof(ProductCategory.Id))!.SetValue(existing, id);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _write.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductCategoryByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductCategoryByTenantAndNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCategory?)null);

        var handler = new UpdateProductCategoryCommandHandler(_tenantContext.Object, _read.Object, _write.Object);
        var result = await handler.Handle(new UpdateProductCategoryCommand(id, "Yeni", "D"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.Name.Should().Be("Yeni");
    }

    [Fact]
    public async Task GetById_NotFound_Should_Fail()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductCategoryByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCategory?)null);

        var handler = new GetProductCategoryByIdQueryHandler(_tenantContext.Object, _read.Object);
        var result = await handler.Handle(new GetProductCategoryByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductCategories.NotFound");
    }

    [Fact]
    public async Task Deactivate_Then_Activate_Should_Succeed()
    {
        var tenantId = Guid.NewGuid();
        var category = new ProductCategory(tenantId, "K");
        var id = category.Id;
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _write.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductCategoryByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var deact = new DeactivateProductCategoryCommandHandler(_tenantContext.Object, _write.Object);
        var act = new ActivateProductCategoryCommandHandler(_tenantContext.Object, _write.Object);

        var d = await deact.Handle(new DeactivateProductCategoryCommand(id), CancellationToken.None);
        var a = await act.Handle(new ActivateProductCategoryCommand(id), CancellationToken.None);

        d.IsSuccess.Should().BeTrue();
        a.IsSuccess.Should().BeTrue();
    }
}
