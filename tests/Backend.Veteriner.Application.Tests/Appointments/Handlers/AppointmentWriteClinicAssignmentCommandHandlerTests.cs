using Backend.Veteriner.Application.Appointments.Commands.Cancel;
using Backend.Veteriner.Application.Appointments.Commands.Complete;
using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Appointments.Commands.Reschedule;
using Backend.Veteriner.Application.Appointments.Commands.Update;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

/// <summary>IDOR-7A: appointment write clinic assignment enforcement unit tests.</summary>
public sealed class AppointmentWriteClinicAssignmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointmentsRead = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettings>> _clinicAppointmentSettings = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _clinicWorkingHoursRead = new();
    private readonly Mock<IRepository<Appointment>> _appointmentsWrite = new();
    private readonly Mock<IAppointmentProjectionSnapshotFactory> _snapshotFactory = new();
    private readonly Mock<IAppointmentIntegrationEventOutbox> _eventOutbox = new();

    public AppointmentWriteClinicAssignmentCommandHandlerTests()
    {
        _clinicWorkingHoursRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>());

        AppointmentHandlerOutboxTestSupport.SetupDefaultOutboxMocks(_snapshotFactory, _eventOutbox);
    }

    private static DateTime SlotAlignedUtcPlusDays(int days)
    {
        var date = DateTime.UtcNow.Date.AddDays(days);
        if (days >= 0)
        {
            while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                date = date.AddDays(1);
        }

        return date.AddHours(9);
    }

    private CreateAppointmentCommandHandler CreateCreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _appointmentsRead.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    private UpdateAppointmentCommandHandler CreateUpdateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _appointmentsRead.Object,
            _clinics.Object,
            _pets.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    private CancelAppointmentCommandHandler CreateCancelHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _appointmentsRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    private CompleteAppointmentCommandHandler CreateCompleteHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _appointmentsRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    private RescheduleAppointmentCommandHandler CreateRescheduleHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _appointmentsRead.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    private void SetupTenantAndClinic(Guid tid, Guid cid)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        var clinic = new Clinic(tid, "K", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        SetupTenantAndClinic(tid, unassignedCid);

        var result = await CreateCreateHandler().Handle(
            new CreateAppointmentCommand(unassignedCid, pid, SlotAlignedUtcPlusDays(2), AppointmentType.Consultation),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointmentsWrite.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_NonTenantWide_AssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantAndClinic(tid, assignedCid);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateAppointmentCommand(assignedCid, pid, SlotAlignedUtcPlusDays(2), AppointmentType.Consultation),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_NonTenantWide_UnassignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantAndClinic(tid, unassignedCid);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateAppointmentCommand(unassignedCid, pid, SlotAlignedUtcPlusDays(2), AppointmentType.Consultation),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _appointmentsWrite.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_ActiveClinicContext_Assigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantAndClinic(tid, assignedCid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(assignedCid);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateAppointmentCommand(null, pid, SlotAlignedUtcPlusDays(2), AppointmentType.Consultation),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_ActiveClinicContext_Unassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCtx = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantAndClinic(tid, unassignedCtx);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(unassignedCtx);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateAppointmentCommand(null, pid, SlotAlignedUtcPlusDays(2), AppointmentType.Consultation),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _appointmentsWrite.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, pid, "P", TestSpeciesIds.Cat, null, null));
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            new UpdateAppointmentCommand(appt.Id, cid, pid, SlotAlignedUtcPlusDays(3), AppointmentType.Vaccination, AppointmentStatus.Scheduled, "n"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_ReturnAccessDenied_When_NonTenantWide_EntityClinicUnassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var appt = new Appointment(tid, entityCid, pid, SlotAlignedUtcPlusDays(1));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            new UpdateAppointmentCommand(appt.Id, entityCid, pid, SlotAlignedUtcPlusDays(3), AppointmentType.Vaccination, AppointmentStatus.Scheduled),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnAccessDenied_When_TargetClinicUnassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var appt = new Appointment(tid, assignedCid, pid, SlotAlignedUtcPlusDays(1));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            new UpdateAppointmentCommand(appt.Id, targetCid, pid, SlotAlignedUtcPlusDays(3), AppointmentType.Vaccination, AppointmentStatus.Scheduled),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Update_Should_NotPull_Entity_To_ActiveClinic_When_EntityInOtherClinic()
    {
        var tid = Guid.NewGuid();
        var activeCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var appt = new Appointment(tid, entityCid, pid, SlotAlignedUtcPlusDays(1));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { activeCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(activeCid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            new UpdateAppointmentCommand(appt.Id, null, pid, SlotAlignedUtcPlusDays(3), AppointmentType.Vaccination, AppointmentStatus.Scheduled),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        appt.ClinicId.Should().Be(entityCid);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_AppointmentMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await CreateUpdateHandler().Handle(
            new UpdateAppointmentCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), AppointmentType.Consultation, AppointmentStatus.Scheduled),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotFound");
    }

    [Fact]
    public async Task Cancel_Should_Succeed_When_NonTenantWide_AssignedEntityClinic()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var appt = new Appointment(tid, cid, Guid.NewGuid(), SlotAlignedUtcPlusDays(1));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateCancelHandler(scope.Object).Handle(new CancelAppointmentCommand(appt.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_Should_ReturnAccessDenied_When_NonTenantWide_UnassignedEntityClinic_EvenWithoutContext()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var appt = new Appointment(tid, entityCid, Guid.NewGuid(), SlotAlignedUtcPlusDays(1));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateCancelHandler(scope.Object).Handle(new CancelAppointmentCommand(appt.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_Should_ReturnAccessDenied_When_UnassignedEntityClinic()
    {
        var tid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var appt = new Appointment(tid, entityCid, Guid.NewGuid(), SlotAlignedUtcPlusDays(1));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(Array.Empty<Guid>());

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateCompleteHandler(scope.Object).Handle(new CompleteAppointmentCommand(appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Reschedule_Should_ReturnAccessDenied_When_UnassignedEntityClinic()
    {
        var tid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var appt = new Appointment(tid, entityCid, Guid.NewGuid(), SlotAlignedUtcPlusDays(1));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(Array.Empty<Guid>());

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateRescheduleHandler(scope.Object).Handle(
            new RescheduleAppointmentCommand(appt.Id, SlotAlignedUtcPlusDays(3)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }
}
