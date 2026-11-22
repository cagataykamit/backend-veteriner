using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Queries.GetCalendar;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Appointments.Resources;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Tests.Visits;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class GetAppointmentsCalendarQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<AppointmentReason>> _reasons = new();
    private readonly Mock<IReadRepository<AppointmentResource>> _resources = new();
    private readonly Mock<IAppointmentReadModelReader> _readModelReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private static readonly Guid VetId = Guid.NewGuid();

    private GetAppointmentsCalendarQueryHandler CreateHandler(
        bool appointmentsQueryEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object,
            _readModelReader.Object,
            Options.Create(new QueryReadModelsOptions { AppointmentsEnabled = appointmentsQueryEnabled }),
            VeterinarianReaderMock.Create(new Dictionary<Guid, string?> { [VetId] = "Dr. Ayşe" }).Object,
            _reasons.Object,
            _resources.Object,
            TimeProvider.System);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var query = new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1));

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_DateWindowMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        var query = new GetAppointmentsCalendarQuery(null, DateTime.UtcNow.AddDays(1));

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.Calendar.DateWindowRequired");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_DateWindowInvalid()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        var now = DateTime.UtcNow;
        var query = new GetAppointmentsCalendarQuery(now, now);

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.Calendar.InvalidDateWindow");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_DateWindowExceedsLimit()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        var query = new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(46));

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.Calendar.DateWindowTooLarge");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_QueryClinic_Differs_From_ActiveClinicContext()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var query = new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), ClinicId: Guid.NewGuid());

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_NoClinicScope_Provided()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ClinicScopeRequired");
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseRequestClinicId_When_NoActiveContext()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), ClinicId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnEmpty_When_NoRows()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_MapRows_WithPetAndClientNames()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);

        var rows = new List<AppointmentCalendarRow>
        {
            new(Guid.NewGuid(), clinicId, petId, DateTime.UtcNow.AddHours(1), 30, AppointmentStatus.Scheduled, AppointmentType.Consultation, VetId),
            new(Guid.NewGuid(), clinicId, petId, DateTime.UtcNow.AddHours(2), 30, AppointmentStatus.Cancelled, AppointmentType.Other, null),
        };
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow> { new(petId, clientId, "Pamuk") });
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Ali Veli") });

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), Status: AppointmentStatus.Scheduled),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].PetName.Should().Be("Pamuk");
        result.Value[0].ClientName.Should().Be("Ali Veli");
        result.Value[0].ClientId.Should().Be(clientId);
        result.Value[0].DurationMinutes.Should().Be(30);
        result.Value[0].ScheduledEndUtc.Should().Be(rows[0].ScheduledAtUtc.AddMinutes(30));
        result.Value[0].VeterinarianUserId.Should().Be(VetId);
        result.Value[0].VeterinarianName.Should().Be("Dr. Ayşe");
        result.Value[1].VeterinarianUserId.Should().BeNull();
        result.Value[1].VeterinarianName.Should().BeNull();
        result.Value[0].State.Should().Be(CalendarItemStateRule.ForAppointment(AppointmentStatus.Scheduled, rows[0].ScheduledAtUtc, DateTime.UtcNow), "kural tek yer");
        result.Value[0].State.Should().NotBeNull();
        result.Value[1].State.Should().BeNull("iptal randevunun durumu yok");
    }

    [Fact]
    public async Task Handle_Should_Apply_Filters_On_Server_Before_Name_Lookup()
    {
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        var keep = new AppointmentCalendarRow(Guid.NewGuid(), clinicId, petId, DateTime.UtcNow.AddDays(2), 30, AppointmentStatus.Scheduled, AppointmentType.Vaccination, VetId);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>
            {
                keep,
                keep with { Id = Guid.NewGuid(), VeterinarianUserId = null },
                keep with { Id = Guid.NewGuid(), AppointmentType = AppointmentType.Surgery },
                keep with { Id = Guid.NewGuid(), Status = AppointmentStatus.Cancelled },
            });
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow>());

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(7),
                VeterinarianUserId: VetId,
                AppointmentTypes: [AppointmentType.Vaccination],
                States: [CalendarItemState.Upcoming]),
            CancellationToken.None);

        result.Value!.Select(i => i.Id).Should().Equal(keep.Id);
    }

    [Fact]
    public async Task Handle_Should_Apply_Filters_On_ReadModel_Path_Too()
    {
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        var assigned = Guid.NewGuid();
        var unassigned = Guid.NewGuid();
        _readModelReader.Setup(r => r.GetCalendarAsync(It.IsAny<AppointmentCalendarReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AppointmentCalendarItemDto(assigned, clinicId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 30, DateTime.UtcNow.AddMinutes(30), AppointmentStatus.Scheduled, AppointmentType.Checkup, "A", "B"),
                new AppointmentCalendarItemDto(unassigned, clinicId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 30, DateTime.UtcNow.AddMinutes(30), AppointmentStatus.Scheduled, AppointmentType.Checkup, "C", "D"),
            });
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentCalendarDetailsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarDetailsRow> { new(assigned, VetId, null, null), new(unassigned, null, null, null) });

        var result = await CreateHandler(appointmentsQueryEnabled: true).Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), WithoutVeterinarian: true),
            CancellationToken.None);

        result.Value!.Select(i => i.Id).Should().Equal(unassigned);
        result.Value!.Single().State.Should().NotBeNull("okuma modeli yolu da durumu verir");
    }

    [Fact]
    public async Task Handle_Should_MapReasonNameAndNotes_ForDetailBubble()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var reasonId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>
            {
                new(Guid.NewGuid(), clinicId, petId, DateTime.UtcNow.AddHours(1), 30, AppointmentStatus.Scheduled, AppointmentType.Vaccination, null, reasonId, "İç ve dış parazit"),
                new(Guid.NewGuid(), clinicId, petId, DateTime.UtcNow.AddHours(2), 30, AppointmentStatus.Scheduled, AppointmentType.Consultation, null),
            });
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow>());
        _reasons.Setup(r => r.ListAsync(It.IsAny<AppointmentReasonNamesByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentReasonNameRow> { new(reasonId, "Aşılama") });

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value![0].ReasonId.Should().Be(reasonId);
        result.Value[0].ReasonName.Should().Be("Aşılama");
        result.Value[0].Notes.Should().Be("İç ve dış parazit");
        result.Value[1].ReasonName.Should().BeNull();
        result.Value[1].Notes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_CompleteDetails_FromCommandDb_When_CqrsEnabled()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var reasonId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _readModelReader.Setup(r => r.GetCalendarAsync(It.IsAny<AppointmentCalendarReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AppointmentCalendarItemDto(appointmentId, clinicId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 30, DateTime.UtcNow.AddMinutes(30),
                    AppointmentStatus.Scheduled, AppointmentType.Checkup, "Pamuk", "Ali Veli"),
            });
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentCalendarDetailsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarDetailsRow> { new(appointmentId, VetId, reasonId, "Kontrol notu") });
        _reasons.Setup(r => r.ListAsync(It.IsAny<AppointmentReasonNamesByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentReasonNameRow> { new(reasonId, "Kontrol") });

        var result = await CreateHandler(appointmentsQueryEnabled: true).Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value![0].VeterinarianName.Should().Be("Dr. Ayşe");
        result.Value[0].ReasonName.Should().Be("Kontrol");
        result.Value[0].Notes.Should().Be("Kontrol notu");
    }

    [Fact]
    public async Task Handle_Should_Complete_Room_And_Equipment_With_Names_On_ReadModel_Path()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var room = AppointmentResource.Create(tenantId, clinicId, "Ameliyathane", AppointmentResourceType.Room, 1, 0, DateTime.UtcNow);
        var device = AppointmentResource.Create(tenantId, clinicId, "Ultrason", AppointmentResourceType.Equipment, 1, 0, DateTime.UtcNow);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _readModelReader.Setup(r => r.GetCalendarAsync(It.IsAny<AppointmentCalendarReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AppointmentCalendarItemDto(appointmentId, clinicId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 30, DateTime.UtcNow.AddMinutes(30),
                    AppointmentStatus.Scheduled, AppointmentType.Checkup, "Pamuk", "Ali Veli"),
            });
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentCalendarDetailsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarDetailsRow> { new(appointmentId, null, null, null, room.Id, device.Id) });
        _resources.Setup(r => r.ListAsync(It.IsAny<AppointmentResourcesByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentResource> { room, device });

        var result = await CreateHandler(appointmentsQueryEnabled: true).Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value![0].RoomResourceId.Should().Be(room.Id);
        result.Value[0].RoomResourceName.Should().Be("Ameliyathane");
        result.Value[0].EquipmentResourceId.Should().Be(device.Id);
        result.Value[0].EquipmentResourceName.Should().Be("Ultrason");
    }

    [Fact]
    public async Task Handle_Should_UseActiveClinicContext_When_ClinicIdNotProvided()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointments.Verify(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_NonTenantWideUser_RequestsAssignedClinicId()
    {
        var tid = Guid.NewGuid();
        var assignedClinicId = Guid.NewGuid();
        var caMock = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedClinicId });
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        var from = DateTime.UtcNow;
        var result = await CreateHandler(scopeResolver: caMock).Handle(
            new GetAppointmentsCalendarQuery(from, from.AddDays(1), ClinicId: assignedClinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        caMock.Verify(x => x.ResolveAsync(tid, assignedClinicId, It.IsAny<CancellationToken>()), Times.Once);
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

        var from = DateTime.UtcNow;
        var result = await CreateHandler(scopeResolver: caMock).Handle(
            new GetAppointmentsCalendarQuery(from, from.AddDays(1), ClinicId: unassignedClinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_TenantWideAdmin_RequestsExplicitClinicId()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        var from = DateTime.UtcNow;
        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(from, from.AddDays(1), ClinicId: clinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scopeResolver.Verify(x => x.ResolveAsync(tid, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_UseQueryReader_WithValidatedClinicId_When_CqrsEnabled()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var caMock = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { clinicId });
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        AppointmentCalendarReadRequest? captured = null;
        _readModelReader.Setup(r => r.GetCalendarAsync(It.IsAny<AppointmentCalendarReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppointmentCalendarReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(Array.Empty<AppointmentCalendarItemDto>());

        var from = DateTime.UtcNow;
        var result = await CreateHandler(appointmentsQueryEnabled: true, scopeResolver: caMock).Handle(
            new GetAppointmentsCalendarQuery(from, from.AddDays(1), ClinicId: clinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Scope.TenantId.Should().Be(tid);
        captured.Scope.ClinicId.Should().Be(clinicId);
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()),
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
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            ct);

        _scopeResolver.Verify(x => x.ResolveAsync(tid, clinicId, ct), Times.Once);
    }
}
