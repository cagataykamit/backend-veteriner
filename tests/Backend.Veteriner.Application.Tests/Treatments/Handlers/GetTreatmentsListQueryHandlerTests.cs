using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Treatments.Queries.GetList;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Treatments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Treatments.Handlers;

public sealed class GetTreatmentsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Treatment>> _treatments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private GetTreatmentsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _treatments.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var paging = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetTreatmentsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _treatments.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<Treatment>>(), It.IsAny<CancellationToken>()),
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

        var result = await CreateHandler().Handle(new GetTreatmentsListQuery(paging, queryClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.ClinicContextMismatch");
        _treatments.Verify(
            r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_NoClinicScope_Provided()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var paging = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetTreatmentsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.ClinicScopeRequired");
        _treatments.Verify(
            r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _treatments.Verify(
            r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseRequestClinicId_When_NoActiveContext()
    {
        var tid = Guid.NewGuid();
        var requestClinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _treatments.Setup(r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _treatments.Setup(r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TreatmentListRow>());

        var paging = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetTreatmentsListQuery(paging, requestClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _treatments.Verify(
            r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_NotQueryClientsOrPetsForSearch_When_SearchIsWhitespace()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _treatments.Setup(r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _treatments.Setup(r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TreatmentListRow>());
        var paging = new PageRequest { Page = 1, PageSize = 20, Search = "   " };

        var result = await CreateHandler().Handle(new GetTreatmentsListQuery(paging), CancellationToken.None);

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
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _treatments.Setup(r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _treatments.Setup(r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TreatmentListRow>());
        var paging = new PageRequest { Page = 1, PageSize = 20, Search = "  ada  " };

        var result = await CreateHandler().Handle(new GetTreatmentsListQuery(paging), CancellationToken.None);

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
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _treatments.Setup(r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _treatments.Setup(r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TreatmentListRow>());
        var paging = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetTreatmentsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        _treatments.Verify(
            r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _treatments.Verify(
            r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_MapRows_When_ItemsExist()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var treatmentId = Guid.NewGuid();
        var treatmentDate = DateTime.UtcNow.AddDays(-1);
        var followUp = DateTime.UtcNow.AddDays(14);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var row = new TreatmentListRow(
            treatmentId,
            clinicId,
            petId,
            treatmentDate,
            "Fizyoterapi",
            examId,
            followUp);

        _treatments.Setup(r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _treatments.Setup(r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TreatmentListRow> { row });

        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Ali Veli") });

        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow> { new(petId, clientId, "Pamuk") });

        var paging = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetTreatmentsListQuery(paging), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(treatmentId);
        item.ClinicId.Should().Be(clinicId);
        item.PetId.Should().Be(petId);
        item.PetName.Should().Be("Pamuk");
        item.ClientId.Should().Be(clientId);
        item.ClientName.Should().Be("Ali Veli");
        item.Title.Should().Be("Fizyoterapi");
        item.TreatmentDateUtc.Should().Be(treatmentDate);
        item.ExaminationId.Should().Be(examId);
        item.FollowUpDateUtc.Should().Be(followUp);
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

        _treatments.Setup(r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _treatments.Setup(r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TreatmentListRow>());

        var paging = new PageRequest { Page = 0, PageSize = 500 };
        var result = await CreateHandler().Handle(
            new GetTreatmentsListQuery(paging, clinicId, petId, dateFrom, dateTo),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
        _treatments.Verify(
            r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _treatments.Verify(
            r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
