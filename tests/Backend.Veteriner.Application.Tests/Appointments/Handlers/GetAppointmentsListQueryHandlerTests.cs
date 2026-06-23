using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
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
    private readonly Mock<IAppointmentReadModelReader> _readModelReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private GetAppointmentsListQueryHandler CreateHandler(
        bool appointmentsQueryEnabled = false,
        bool sharedSearchLookupEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object,
            _clinics.Object,
            _readModelReader.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                AppointmentsEnabled = appointmentsQueryEnabled,
                SharedSearchLookupEnabled = sharedSearchLookupEnabled
            }));

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
    public async Task Handle_Should_Fail_When_NoClinicScope_Provided()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetAppointmentsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ClinicScopeRequired");
        _appointments.Verify(
            r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseRequestClinicId_When_NoActiveContext()
    {
        var tid = Guid.NewGuid();
        var requestClinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetAppointmentsListQuery(page, requestClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointments.Verify(
            r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ClampPageAndPageSize()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
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
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
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
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
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
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled,
            "not");

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
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
        dto.DurationMinutes.Should().Be(30);
        dto.ScheduledEndUtc.Should().Be(appointment.ScheduledEndUtc);
    }

    [Fact]
    public async Task Handle_Should_MapClinicName_When_ScopedClinicContextMatchesAllRows()
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
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled,
            null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { appointment });
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientSpeciesRow>
            {
                new(petId, clientId, "Dost", TestSpeciesIds.Cat, "Kedi")
            });
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Veli") });
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicNameRow> { new(clinicId, "Şube A") });

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetAppointmentsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle()
            .Which.ClinicName.Should().Be("Şube A");
        _clinics.Verify(
            r => r.ListAsync(It.IsAny<ClinicsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_NonTenantWideUser_RequestsAssignedClinicId()
    {
        var tid = Guid.NewGuid();
        var assignedClinicId = Guid.NewGuid();
        var caMock = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedClinicId });
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler(scopeResolver: caMock)
            .Handle(new GetAppointmentsListQuery(page, assignedClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        caMock.Verify(
            x => x.ResolveAsync(tid, assignedClinicId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_NonTenantWideUser_RequestsUnassignedClinicId()
    {
        var tid = Guid.NewGuid();
        var assignedClinicId = Guid.NewGuid();
        var unassignedClinicId = Guid.NewGuid();
        var caMock = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedClinicId });
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler(scopeResolver: caMock)
            .Handle(new GetAppointmentsListQuery(page, unassignedClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _appointments.Verify(
            r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _readModelReader.Verify(
            r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_TenantWideAdmin_RequestsExplicitClinicId()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler()
            .Handle(new GetAppointmentsListQuery(page, clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scopeResolver.Verify(
            x => x.ResolveAsync(tid, clinicId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ResolverReturnsNotFound_WithoutQueryingRepository()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var caMock = ClinicReadScopeResolverMock.Default();
        caMock.SetupNotFound();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler(scopeResolver: caMock)
            .Handle(new GetAppointmentsListQuery(page, clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
        _appointments.Verify(
            r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _readModelReader.Verify(
            r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseQueryDb_WithValidatedClinicId_When_CqrsEnabledAndAssigned()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var caMock = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { clinicId });
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        AppointmentListReadRequest? captured = null;
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppointmentListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new AppointmentListReadResult(Array.Empty<AppointmentListItemDto>(), 0));

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler(appointmentsQueryEnabled: true, scopeResolver: caMock)
            .Handle(new GetAppointmentsListQuery(page, clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Scope.TenantId.Should().Be(tid);
        captured.Scope.ClinicId.Should().Be(clinicId);
        _appointments.Verify(
            r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_NotCallQueryDb_When_CqrsEnabledAndUnassignedClinicId()
    {
        var tid = Guid.NewGuid();
        var assignedClinicId = Guid.NewGuid();
        var unassignedClinicId = Guid.NewGuid();
        var caMock = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedClinicId });
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler(appointmentsQueryEnabled: true, scopeResolver: caMock)
            .Handle(new GetAppointmentsListQuery(page, unassignedClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _readModelReader.Verify(
            r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_PropagateCancellationToken_ToResolver()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var ct = new CancellationTokenSource().Token;
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        await CreateHandler().Handle(new GetAppointmentsListQuery(new PageRequest()), ct);

        _scopeResolver.Verify(x => x.ResolveAsync(tid, clinicId, ct), Times.Once);
    }
}
