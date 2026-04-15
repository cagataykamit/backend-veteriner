using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Payments.Queries.GetList;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments.Handlers;

public sealed class GetPaymentsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetPaymentsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _payments.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var paging = new PaymentListPagingRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetPaymentsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _payments.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<Payment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_NotQueryClientsOrPetsForSearch_When_SearchIsWhitespace()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        var paging = new PaymentListPagingRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetPaymentsListQuery(paging, Search: "   "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_QueryClientsAndPets_When_SearchProvided()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        var paging = new PaymentListPagingRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetPaymentsListQuery(paging, Search: "  ada  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_UseTenantScopedSpecs_When_ContextPresent()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        var paging = new PaymentListPagingRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetPaymentsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_MapRows_When_ItemsExist()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);

        var payment = new Payment(
            tid,
            clinicId,
            clientId,
            petId,
            null,
            null,
            250m,
            "TRY",
            PaymentMethod.Card,
            DateTime.UtcNow.AddHours(-2),
            "Not");

        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { payment });

        var client = new Client(tid, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client> { client });

        var pet = new Pet(tid, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet> { pet });

        var paging = new PaymentListPagingRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetPaymentsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(payment.Id);
        item.ClinicId.Should().Be(clinicId);
        item.ClientId.Should().Be(clientId);
        item.ClientName.Should().Be("Ali Veli");
        item.PetId.Should().Be(petId);
        item.PetName.Should().Be("Pamuk");
        item.Amount.Should().Be(250m);
        item.Currency.Should().Be("TRY");
        item.Method.Should().Be(PaymentMethod.Card);
    }

    [Fact]
    public async Task Handle_Should_ApplyFilterCombination_And_ClampPaging()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var paidFrom = DateTime.UtcNow.AddDays(-2);
        var paidTo = DateTime.UtcNow;
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);

        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        var paging = new PaymentListPagingRequest { Page = 0, PageSize = 500 };
        var result = await CreateHandler().Handle(
            new GetPaymentsListQuery(paging, clinicId, clientId, petId, PaymentMethod.Transfer, paidFrom, paidTo, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_QueryClinic_Differs_From_ActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var queryClinicId = Guid.NewGuid();
        var paging = new PaymentListPagingRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetPaymentsListQuery(paging, queryClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ClinicContextMismatch");
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
