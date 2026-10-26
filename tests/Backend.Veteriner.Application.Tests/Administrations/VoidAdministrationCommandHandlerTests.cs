using Backend.Veteriner.Domain.Vaccinations;
using Ardalis.Specification;
using Backend.Veteriner.Application.Administrations.Commands.Void;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockMovements.Commands.Reverse;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Administrations;

public sealed class VoidAdministrationCommandHandlerTests
{
    private readonly Mock<IRepository<Administration>> _administrations = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IReadRepository<StockMovement>> _stockMovements = new();
    private readonly Mock<IRepository<ChargeSuggestion>> _chargeSuggestions = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();
    private readonly Administration _administration;
    private readonly StockMovement _out;

    public VoidAdministrationCommandHandlerTests()
    {
        _administration = Administration.Create(
            _tenantId, _clinicId, Guid.NewGuid(), null, null, _productId, 2m, DateTime.UtcNow,
            null, false, null, Guid.NewGuid(), DateTime.UtcNow, null).Value!;
        _out = new StockMovement(
            _tenantId, _clinicId, _productId, StockMovementType.Out, 2m, DateTime.UtcNow,
            referenceType: "Administration", referenceId: _administration.Id);

        _administrations
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Administration>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_administration);
        _stockMovements
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<StockMovement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_out);
    }

    private VoidAdministrationCommandHandler CreateHandler(bool productActive = true)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        var clinicContext = new Mock<IClinicContext>();
        clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var clientContext = new Mock<IClientContext>();
        clientContext.SetupGet(c => c.UserId).Returns(Guid.NewGuid());

        var tenants = new Mock<IReadRepository<Tenant>>();
        tenants
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Tenant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var product = new Product(_tenantId, "Ürün", "Adet", 10m, "TRY", sku: "SKU-1");
        if (!productActive)
            product.Deactivate();
        var products = new Mock<IReadRepository<Product>>();
        products
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _sender
            .Setup(s => s.Send(It.IsAny<ReverseStockMovementCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StockMovementCreateResult>.Success(null!));

        return new VoidAdministrationCommandHandler(
            tenantContext.Object,
            clinicContext.Object,
            clientContext.Object,
            ClinicReadScopeResolverMock.Default().Object,
            tenants.Object,
            products.Object,
            _stockMovements.Object,
            new Mock<IReadRepository<StockBatch>>().Object,
            _administrations.Object,
            new Mock<IRepository<Vaccination>>().Object,
            _chargeSuggestions.Object,
            _sender.Object,
            TimeProvider.System);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("StockReturned")]
    [InlineData("stockreturned")]
    public async Task Handle_Should_Reverse_Stock_And_Default_To_StockReturned(string? outcome)
    {
        var result = await CreateHandler(productActive: false).Handle(
            new VoidAdministrationCommand(_administration.Id, "Yanlış ürün seçildi", outcome), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.VoidStockOutcome.Should().Be("StockReturned");
        _sender.Verify(s => s.Send(
            It.Is<ReverseStockMovementCommand>(c => c.MovementId == _out.Id), It.IsAny<CancellationToken>()), Times.Once);
        _administrations.Verify(r => r.UpdateAsync(It.IsAny<Administration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WrittenOff_Should_Skip_Stock_And_Persist_Void()
    {
        var result = await CreateHandler().Handle(
            new VoidAdministrationCommand(_administration.Id, "Şişe kırıldı, imha edildi", "WrittenOff"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Voided");
        result.Value.VoidStockOutcome.Should().Be("WrittenOff");
        _sender.Verify(s => s.Send(It.IsAny<ReverseStockMovementCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        _administrations.Verify(r => r.UpdateAsync(_administration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WrittenOff_Should_Map_Concurrency_Loss_To_AlreadyVoided()
    {
        _administrations
            .Setup(r => r.UpdateAsync(It.IsAny<Administration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict.", innerException: null));

        var result = await CreateHandler().Handle(
            new VoidAdministrationCommand(_administration.Id, "Şişe kırıldı, imha edildi", "WrittenOff"), CancellationToken.None);

        result.Error!.Code.Should().Be("Administrations.AlreadyVoided");
    }

    [Fact]
    public async Task Handle_Should_Drop_Pending_Charge_Suggestion()
    {
        var suggestion = SetupSuggestion();

        var result = await CreateHandler().Handle(
            new VoidAdministrationCommand(_administration.Id, "Yanlış ürün seçildi", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        suggestion.Status.Should().Be(ChargeSuggestionStatus.Cancelled);
        _chargeSuggestions.Verify(r => r.UpdateAsync(suggestion, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Leave_Converted_Charge_Suggestion_Untouched()
    {
        var suggestion = SetupSuggestion();
        suggestion.MarkConverted(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        var result = await CreateHandler().Handle(
            new VoidAdministrationCommand(_administration.Id, "Yanlış ürün seçildi", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        suggestion.Status.Should().Be(ChargeSuggestionStatus.Converted);
        _chargeSuggestions.Verify(r => r.UpdateAsync(It.IsAny<ChargeSuggestion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Suggestion_Changed_Concurrently()
    {
        SetupSuggestion();
        _chargeSuggestions
            .Setup(r => r.UpdateAsync(It.IsAny<ChargeSuggestion>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict.", innerException: null));

        var result = await CreateHandler().Handle(
            new VoidAdministrationCommand(_administration.Id, "Yanlış ürün seçildi", null), CancellationToken.None);

        result.Error!.Code.Should().Be("Administrations.ConcurrencyConflict");
    }

    private ChargeSuggestion SetupSuggestion()
    {
        var product = new Product(_tenantId, "Ürün", "Adet", 10m, "TRY", sku: "SKU-1");
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(product, _productId);
        var suggestion = ChargeSuggestion.ForAdministration(_administration, product, null).Value!;
        _chargeSuggestions
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ChargeSuggestion>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestion);
        return suggestion;
    }

    [Theory]
    [InlineData("Lost")]
    [InlineData("0")]
    [InlineData("3")]
    public async Task Handle_Should_Reject_Unknown_Outcome_Before_Any_Write(string outcome)
    {
        var result = await CreateHandler().Handle(
            new VoidAdministrationCommand(_administration.Id, "Geçersiz sonuç denemesi", outcome), CancellationToken.None);

        result.Error!.Code.Should().Be("Administrations.Validation");
        _administration.IsVoided.Should().BeFalse();
        _sender.Verify(s => s.Send(It.IsAny<ReverseStockMovementCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
