using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Hospitalizations.Queries.GetList;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Handlers;

public sealed class GetHospitalizationsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetHospitalizationsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _hospitalizations.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var paging = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetHospitalizationsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _hospitalizations.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<Hospitalization>>(), It.IsAny<CancellationToken>()),
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

        var result = await CreateHandler().Handle(new GetHospitalizationsListQuery(paging, queryClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ClinicContextMismatch");
        _hospitalizations.Verify(
            r => r.CountAsync(It.IsAny<HospitalizationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_NotQueryClientsOrPetsForSearch_When_SearchIsWhitespace()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _hospitalizations.Setup(r => r.CountAsync(It.IsAny<HospitalizationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _hospitalizations.Setup(r => r.ListAsync(It.IsAny<HospitalizationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Hospitalization>());
        var paging = new PageRequest { Page = 1, PageSize = 20, Search = "   " };

        var result = await CreateHandler().Handle(new GetHospitalizationsListQuery(paging), CancellationToken.None);

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
        _hospitalizations.Setup(r => r.CountAsync(It.IsAny<HospitalizationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _hospitalizations.Setup(r => r.ListAsync(It.IsAny<HospitalizationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Hospitalization>());
        var paging = new PageRequest { Page = 1, PageSize = 20, Search = "  yatış  " };

        var result = await CreateHandler().Handle(new GetHospitalizationsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_ActiveOnlyTrue()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _hospitalizations.Setup(r => r.CountAsync(It.IsAny<HospitalizationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _hospitalizations.Setup(r => r.ListAsync(It.IsAny<HospitalizationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Hospitalization>());
        var paging = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetHospitalizationsListQuery(paging, ActiveOnly: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_MapRows_And_IsActive_When_ItemsExist()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);

        var h = new Hospitalization(
            tid,
            clinicId,
            petId,
            examId,
            DateTime.UtcNow.AddDays(-2),
            DateTime.UtcNow.AddDays(1),
            "Gözlem",
            null);

        _hospitalizations.Setup(r => r.CountAsync(It.IsAny<HospitalizationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _hospitalizations.Setup(r => r.ListAsync(It.IsAny<HospitalizationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Hospitalization> { h });

        var client = new Client(tid, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client> { client });

        var pet = new Pet(tid, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet> { pet });

        var paging = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetHospitalizationsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(h.Id);
        item.IsActive.Should().BeTrue();
        item.Reason.Should().Be("Gözlem");
        item.ExaminationId.Should().Be(examId);
    }

    [Fact]
    public async Task Handle_Should_ApplyFilterCombination_And_ClampPaging()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var dateFrom = DateTime.UtcNow.AddDays(-5);
        var dateTo = DateTime.UtcNow;
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);

        _hospitalizations.Setup(r => r.CountAsync(It.IsAny<HospitalizationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _hospitalizations.Setup(r => r.ListAsync(It.IsAny<HospitalizationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Hospitalization>());

        var paging = new PageRequest { Page = 0, PageSize = 500 };
        var result = await CreateHandler().Handle(
            new GetHospitalizationsListQuery(paging, clinicId, petId, ActiveOnly: false, dateFrom, dateTo),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
    }
}
