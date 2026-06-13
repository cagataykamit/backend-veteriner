using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductStocks.Queries.GetByProductId;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.ProductStocks.Handlers;

public sealed class GetProductStocksByProductIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Product>> _products = new();
    private readonly Mock<IReadRepository<ProductCategory>> _categories = new();
    private readonly Mock<IReadRepository<ProductStock>> _productStocks = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private GetProductStocksByProductIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _products.Object,
            _categories.Object,
            _productStocks.Object,
            _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var pid = Guid.NewGuid();

        var result = await CreateHandler().Handle(new GetProductStocksByProductIdQuery(pid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _productStocks.Verify(
            r => r.ListAsync(It.IsAny<ProductStocksForProductReadSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_NoClinicScope_Provided()
    {
        var tid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var product = new Product(tid, "Serum", "Adet", 3m, "TRY");
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var result = await CreateHandler().Handle(new GetProductStocksByProductIdQuery(pid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductStocks.ClinicScopeRequired");
        _productStocks.Verify(
            r => r.ListAsync(It.IsAny<ProductStocksForProductReadSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _scopeResolver.Verify(
            x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnProductsNotFound_When_ProductMissing()
    {
        var tid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var result = await CreateHandler().Handle(new GetProductStocksByProductIdQuery(pid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.NotFound");
        _productStocks.Verify(
            r => r.ListAsync(It.IsAny<ProductStocksForProductReadSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnMappedRows_When_ProductExists()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var product = new Product(tid, "Serum", "Adet", 3m, "TRY");
        var stock = new ProductStock(tid, clinicId, product.Id, 10m, 2m);
        typeof(ProductStock).GetProperty(nameof(ProductStock.Product))!.SetValue(stock, product);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _categories.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductCategory>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCategory?)null);
        _productStocks.Setup(r => r.ListAsync(It.IsAny<ProductStocksForProductReadSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductStock> { stock });
        _clinics.Setup(r => r.ListAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>
            {
                CreateClinicReflection(clinicId, tid, "Depo")
            });

        var result = await CreateHandler().Handle(new GetProductStocksByProductIdQuery(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Should().ContainSingle().Subject;
        dto.ProductId.Should().Be(product.Id);
        dto.ClinicName.Should().Be("Depo");
        dto.IsBelowMinimum.Should().BeFalse();
    }

    private static Clinic CreateClinicReflection(Guid id, Guid tenantId, string name)
    {
        var clinic = new Clinic(tenantId, name, "Ankara");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        return clinic;
    }
}
