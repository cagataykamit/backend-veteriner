using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Prescriptions.Queries.GetList;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Prescriptions.Handlers;

public sealed class GetPrescriptionsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Prescription>> _prescriptions = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetPrescriptionsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _prescriptions.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var paging = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetPrescriptionsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _prescriptions.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<Prescription>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_QueryClinic_Differs_From_ActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var queryClinicId = Guid.NewGuid();
        var paging = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetPrescriptionsListQuery(paging, queryClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Prescriptions.ClinicContextMismatch");
        _prescriptions.Verify(
            r => r.CountAsync(It.IsAny<PrescriptionsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_NotQueryClientsOrPetsForSearch_When_SearchIsWhitespace()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _prescriptions.Setup(r => r.CountAsync(It.IsAny<PrescriptionsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _prescriptions.Setup(r => r.ListAsync(It.IsAny<PrescriptionsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());
        var paging = new PageRequest { Page = 1, PageSize = 20, Search = "   " };

        var result = await CreateHandler().Handle(new GetPrescriptionsListQuery(paging), CancellationToken.None);

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
        _prescriptions.Setup(r => r.CountAsync(It.IsAny<PrescriptionsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _prescriptions.Setup(r => r.ListAsync(It.IsAny<PrescriptionsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());
        var paging = new PageRequest { Page = 1, PageSize = 20, Search = "  ada  " };

        var result = await CreateHandler().Handle(new GetPrescriptionsListQuery(paging), CancellationToken.None);

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
        _prescriptions.Setup(r => r.CountAsync(It.IsAny<PrescriptionsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _prescriptions.Setup(r => r.ListAsync(It.IsAny<PrescriptionsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());
        var paging = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetPrescriptionsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        _prescriptions.Verify(
            r => r.CountAsync(It.IsAny<PrescriptionsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _prescriptions.Verify(
            r => r.ListAsync(It.IsAny<PrescriptionsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
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

        var pr = new Prescription(
            tid,
            clinicId,
            petId,
            null,
            null,
            DateTime.UtcNow.AddDays(-1),
            "Antibiyotik",
            "İçerik metni",
            null,
            DateTime.UtcNow.AddDays(7));

        _prescriptions.Setup(r => r.CountAsync(It.IsAny<PrescriptionsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _prescriptions.Setup(r => r.ListAsync(It.IsAny<PrescriptionsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription> { pr });

        var client = new Client(tid, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client> { client });

        var pet = new Pet(tid, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet> { pet });

        var paging = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetPrescriptionsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(pr.Id);
        item.ClinicId.Should().Be(clinicId);
        item.PetId.Should().Be(petId);
        item.PetName.Should().Be("Pamuk");
        item.ClientId.Should().Be(clientId);
        item.ClientName.Should().Be("Ali Veli");
        item.Title.Should().Be("Antibiyotik");
        item.PrescribedAtUtc.Should().Be(pr.PrescribedAtUtc);
        item.FollowUpDateUtc.Should().Be(pr.FollowUpDateUtc);
    }

    [Fact]
    public async Task Handle_Should_ApplyFilterCombination_And_ClampPaging()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var dateFrom = DateTime.UtcNow.AddDays(-2);
        var dateTo = DateTime.UtcNow;
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);

        _prescriptions.Setup(r => r.CountAsync(It.IsAny<PrescriptionsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _prescriptions.Setup(r => r.ListAsync(It.IsAny<PrescriptionsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());

        var paging = new PageRequest { Page = 0, PageSize = 500 };
        var result = await CreateHandler().Handle(
            new GetPrescriptionsListQuery(paging, clinicId, petId, dateFrom, dateTo),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
        _prescriptions.Verify(
            r => r.CountAsync(It.IsAny<PrescriptionsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _prescriptions.Verify(
            r => r.ListAsync(It.IsAny<PrescriptionsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
