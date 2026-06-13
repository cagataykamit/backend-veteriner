using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ProductStocks.Queries.GetList;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.ProductStocks.Handlers;

public sealed class GetProductStocksListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<ProductStock>> _productStocks = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private GetProductStocksListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _productStocks.Object,
            _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetProductStocksListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _productStocks.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_NoClinicScope_Provided()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetProductStocksListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductStocks.ClinicScopeRequired");
        _productStocks.Verify(
            r => r.CountAsync(It.IsAny<ProductStocksFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _productStocks.Verify(
            r => r.ListAsync(It.IsAny<ProductStocksFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseRequestClinicId_When_NoActiveContext()
    {
        var tid = Guid.NewGuid();
        var requestClinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _productStocks.Setup(r => r.CountAsync(It.IsAny<ProductStocksFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _productStocks.Setup(r => r.ListAsync(It.IsAny<ProductStocksFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductStock>());

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(
            new GetProductStocksListQuery(page, ClinicId: requestClinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _productStocks.Verify(
            r => r.CountAsync(It.IsAny<ProductStocksFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_RequestClinic_Does_Not_Match_ActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var queryClinic = Guid.NewGuid();
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(
            new GetProductStocksListQuery(page, ClinicId: queryClinic),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductStocks.ClinicContextMismatch");
        _productStocks.Verify(
            r => r.CountAsync(It.IsAny<ProductStocksFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_PropagateScopeFailure_When_ResolverFails()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _scopeResolver.SetupAccessDenied();
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(
            new GetProductStocksListQuery(page, ClinicId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _productStocks.Verify(
            r => r.CountAsync(It.IsAny<ProductStocksFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicAdmin_Without_ClinicScope()
    {
        var tid = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var resolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assigned, c2 });
        var handler = new GetProductStocksListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver.Object,
            _productStocks.Object,
            _clinics.Object);

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await handler.Handle(new GetProductStocksListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductStocks.ClinicScopeRequired");
        _productStocks.Verify(
            r => r.CountAsync(It.IsAny<ProductStocksFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        resolver.Verify(
            x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_MapRows_And_IsBelowMinimum()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var product = new Product(tid, "VAC", "Adet", 10m, "TRY");
        AttachProductReflection(product);
        var stock = new ProductStock(tid, clinicId, product.Id, quantityOnHand: 2m, minimumStockLevel: 5m);
        AttachStockProductReflection(stock, product);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _productStocks.Setup(r => r.CountAsync(It.IsAny<ProductStocksFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _productStocks.Setup(r => r.ListAsync(It.IsAny<ProductStocksFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductStock> { stock });
        _clinics.Setup(r => r.ListAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>
            {
                CreateClinicReflection(clinicId, tid, "Merkez Subesi")
            });

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetProductStocksListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Items.Should().ContainSingle().Subject;
        dto.ProductName.Should().Be("VAC");
        dto.ClinicId.Should().Be(clinicId);
        dto.ClinicName.Should().Be("Merkez Subesi");
        dto.IsBelowMinimum.Should().BeTrue();
        dto.QuantityOnHand.Should().Be(2);
        dto.MinimumStockLevel.Should().Be(5);
    }

    private static void AttachProductReflection(Product product)
    {
        typeof(Product).GetProperty(nameof(Product.Category))!
            .SetValue(product, null);
    }

    private static void AttachStockProductReflection(ProductStock stock, Product product)
    {
        typeof(ProductStock).GetProperty(nameof(ProductStock.Product))!
            .SetValue(stock, product);
    }

    private static Clinic CreateClinicReflection(Guid id, Guid tenantId, string name)
    {
        var clinic = new Clinic(tenantId, name, "Istanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        return clinic;
    }
}
