using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Handlers;

public sealed class GetVaccinationsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetVaccinationsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _vaccinations.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetVaccinationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _vaccinations.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseTenantScopedSpecs_When_ContextPresent()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _vaccinations.Setup(r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination>());
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetVaccinationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        _vaccinations.Verify(
            r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _vaccinations.Verify(
            r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_NotQuerySearchLookups_When_SearchWhitespaceOnly()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _vaccinations.Setup(r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination>());

        var page = new PageRequest { Page = 1, PageSize = 20, Search = "   " };
        var result = await CreateHandler().Handle(new GetVaccinationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_QuerySearchLookups_When_SearchProvided()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _vaccinations.Setup(r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination>());

        var page = new PageRequest { Page = 1, PageSize = 20, Search = "  kuduz  " };
        var result = await CreateHandler().Handle(new GetVaccinationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_MapRows_When_ItemsExist()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var examinationId = Guid.NewGuid();
        var due = DateTime.UtcNow.AddDays(5);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);

        var entity = new Vaccination(
            tid,
            petId,
            clinicId,
            examinationId,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            due,
            null);

        _vaccinations.Setup(r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination> { entity });
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow> { new(petId, clientId, "Pamuk") });
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Ali Veli") });

        var result = await CreateHandler().Handle(new GetVaccinationsListQuery(new PageRequest { Page = 1, PageSize = 20 }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Items.Should().ContainSingle().Subject;
        dto.Id.Should().Be(entity.Id);
        dto.ClinicId.Should().Be(clinicId);
        dto.PetId.Should().Be(petId);
        dto.PetName.Should().Be("Pamuk");
        dto.ClientId.Should().Be(clientId);
        dto.ClientName.Should().Be("Ali Veli");
        dto.ExaminationId.Should().Be(examinationId);
        dto.VaccineName.Should().Be("Kuduz");
        dto.Status.Should().Be(VaccinationStatus.Scheduled);
        dto.DueAtUtc.Should().Be(due);
    }

    [Fact]
    public async Task Handle_Should_ApplyStatusDateFilters_And_ClampPaging()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var dueFrom = DateTime.UtcNow.AddDays(-1);
        var dueTo = DateTime.UtcNow.AddDays(14);
        var appliedFrom = DateTime.UtcNow.AddDays(-3);
        var appliedTo = DateTime.UtcNow;

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _vaccinations.Setup(r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination>());

        var page = new PageRequest { Page = 0, PageSize = 500 };
        var result = await CreateHandler().Handle(
            new GetVaccinationsListQuery(page, clinicId, petId, VaccinationStatus.Applied, dueFrom, dueTo, appliedFrom, appliedTo),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
        _vaccinations.Verify(
            r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _vaccinations.Verify(
            r => r.ListAsync(It.IsAny<VaccinationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_QueryClinic_Differs_From_ActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var queryClinicId = Guid.NewGuid();
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetVaccinationsListQuery(page, queryClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.ClinicContextMismatch");
        _vaccinations.Verify(
            r => r.CountAsync(It.IsAny<VaccinationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
