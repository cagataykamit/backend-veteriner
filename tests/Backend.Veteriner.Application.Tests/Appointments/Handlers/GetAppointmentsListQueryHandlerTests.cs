using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class GetAppointmentsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private GetAppointmentsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object,
            _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetAppointmentsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _appointments.Verify(
            r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_QueryClinic_Differs_From_ActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var queryClinicId = Guid.NewGuid();
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetAppointmentsListQuery(page, queryClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ClinicContextMismatch");
        _appointments.Verify(
            r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ClampPageAndPageSize()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var query = new GetAppointmentsListQuery(new PageRequest { Page = 0, PageSize = 500 });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task Handle_Should_SearchAcrossPetsAndClients_When_SearchProvided()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client> { new(tid, "Ali Veli") });
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20, Search = "  ada  " });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_NotQueryTextLookups_When_SearchWhitespaceOnly()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20, Search = "   " });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_MapListItems_When_RowsExist()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var appointment = new Appointment(
            tid,
            clinicId,
            petId,
            DateTime.UtcNow.AddDays(1),
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled,
            "not");

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { appointment });
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientSpeciesRow>
            {
                new(petId, clientId, "Pamuk", TestSpeciesIds.Cat, "Kedi")
            });
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Ali Veli") });
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow> { new(clinicId, "Merkez Klinik") });

        var page = new PageRequest { Page = 1, PageSize = 20, Sort = "ScheduledAtUtc", Order = "asc" };
        var result = await CreateHandler().Handle(new GetAppointmentsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Items.Should().ContainSingle().Subject;
        dto.ClinicName.Should().Be("Merkez Klinik");
        dto.PetName.Should().Be("Pamuk");
        dto.ClientId.Should().Be(clientId);
        dto.ClientName.Should().Be("Ali Veli");
        dto.SpeciesId.Should().Be(TestSpeciesIds.Cat);
        dto.SpeciesName.Should().Be("Kedi");
        dto.AppointmentType.Should().Be(AppointmentType.Consultation);
        dto.Status.Should().Be(AppointmentStatus.Scheduled);
        dto.Notes.Should().Be("not");
    }
}
