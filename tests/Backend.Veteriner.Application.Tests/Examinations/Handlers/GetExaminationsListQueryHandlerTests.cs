using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class GetExaminationsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetExaminationsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _examinations.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetExaminationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _examinations.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<Examination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_QueryWithTenantScopedSpecs_When_ContextPresent()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetExaminationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value.TotalItems.Should().Be(0);
        _examinations.Verify(
            r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _examinations.Verify(
            r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_NotQuerySearchLookups_When_SearchWhitespaceOnly()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());

        var page = new PageRequest { Page = 1, PageSize = 20, Search = "   " };
        var result = await CreateHandler().Handle(new GetExaminationsListQuery(page), CancellationToken.None);

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
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());

        var page = new PageRequest { Page = 1, PageSize = 20, Search = "  kontrol  " };
        var result = await CreateHandler().Handle(new GetExaminationsListQuery(page), CancellationToken.None);

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
        var examId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var examined = DateTime.UtcNow;

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>
            {
                new(examId, clinicId, petId, appointmentId, examined, "Kontrol")
            });
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow> { new(petId, clientId, "Pamuk") });
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Ali Veli") });

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetExaminationsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Items.Should().ContainSingle().Subject;
        dto.Id.Should().Be(examId);
        dto.ClinicId.Should().Be(clinicId);
        dto.PetId.Should().Be(petId);
        dto.PetName.Should().Be("Pamuk");
        dto.ClientId.Should().Be(clientId);
        dto.ClientName.Should().Be("Ali Veli");
        dto.AppointmentId.Should().Be(appointmentId);
        dto.ExaminedAtUtc.Should().Be(examined);
        dto.VisitReason.Should().Be("Kontrol");
    }

    [Fact]
    public async Task Handle_Should_ApplyFilterCombination_And_ClampPaging()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var dateFrom = DateTime.UtcNow.AddDays(-1);
        var dateTo = DateTime.UtcNow;

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());

        var page = new PageRequest { Page = 0, PageSize = 500 };
        var result = await CreateHandler().Handle(
            new GetExaminationsListQuery(page, clinicId, petId, appointmentId, dateFrom, dateTo),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
        _examinations.Verify(
            r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _examinations.Verify(
            r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
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

        var result = await CreateHandler().Handle(new GetExaminationsListQuery(page, queryClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.ClinicContextMismatch");
        _examinations.Verify(
            r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
