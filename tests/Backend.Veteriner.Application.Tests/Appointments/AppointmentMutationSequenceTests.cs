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

namespace Backend.Veteriner.Application.Tests.Appointments;

public sealed class AppointmentMutationSequenceTests
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

    public AppointmentMutationSequenceTests()
    {
        _clinicWorkingHoursRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>());
    }

    [Fact]
    public async Task Create_Should_Set_Sequence_One_On_Entity_And_Event()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(1);
        Appointment? capturedAppointment = null;
        AppointmentCreatedIntegrationEvent? capturedEvent = null;

        SetupCreateSuccess(tid, cid, pid);
        _appointmentsWrite.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .Callback<Appointment, CancellationToken>((a, _) => capturedAppointment = a);
        _snapshotFactory.Setup(f => f.CreateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment a, CancellationToken _) => AppointmentHandlerOutboxTestSupport.CreateSnapshot(a));
        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Created,
                It.IsAny<AppointmentCreatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentCreatedIntegrationEvent, CancellationToken>((_, e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        var handler = new CreateAppointmentCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _appointmentsRead.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

        var result = await handler.Handle(
            new CreateAppointmentCommand(cid, pid, when, AppointmentType.Vaccination, null, "note"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedAppointment!.MutationSequence.Should().Be(1);
        capturedEvent!.AppointmentSequence.Should().Be(1);
        capturedEvent.AppointmentId.Should().Be(capturedAppointment.Id);
    }

    [Fact]
    public async Task Reschedule_Should_Carry_Matching_Sequence_On_Event()
    {
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1));
        appt.AdvanceMutationSequence();
        AppointmentRescheduledIntegrationEvent? captured = null;

        SetupRescheduleSuccess(tid, appt, SlotAlignedUtcPlusDays(3));
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt));
        _snapshotFactory.Setup(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), It.IsAny<AppointmentProjectionSnapshot>()))
            .Returns((Appointment a, AppointmentProjectionSnapshot p) => p with { ScheduledAtUtc = a.ScheduledAtUtc });
        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Rescheduled,
                It.IsAny<AppointmentRescheduledIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentRescheduledIntegrationEvent, CancellationToken>((_, e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var handler = new RescheduleAppointmentCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _appointmentsRead.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

        var result = await handler.Handle(new RescheduleAppointmentCommand(appt.Id, SlotAlignedUtcPlusDays(3)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.MutationSequence.Should().Be(2);
        captured!.AppointmentSequence.Should().Be(2);
        captured.AppointmentId.Should().Be(appt.Id);
    }

    [Fact]
    public async Task Cancel_Should_Map_Sequence_To_Event()
    {
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1));
        AppointmentCancelledIntegrationEvent? captured = null;

        SetupLifecycleRead(tid, appt);
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt));
        _snapshotFactory.Setup(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), It.IsAny<AppointmentProjectionSnapshot>()))
            .Returns((Appointment a, AppointmentProjectionSnapshot p) => p with { Status = (int)a.Status });
        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Cancelled,
                It.IsAny<AppointmentCancelledIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentCancelledIntegrationEvent, CancellationToken>((_, e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var handler = new CancelAppointmentCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _appointmentsRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

        var result = await handler.Handle(new CancelAppointmentCommand(appt.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.AppointmentSequence.Should().Be(appt.MutationSequence);
    }

    [Fact]
    public async Task Complete_Should_Map_Sequence_To_Event()
    {
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1));
        AppointmentCompletedIntegrationEvent? captured = null;

        SetupLifecycleRead(tid, appt);
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt));
        _snapshotFactory.Setup(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), It.IsAny<AppointmentProjectionSnapshot>()))
            .Returns((Appointment a, AppointmentProjectionSnapshot p) => p with { Status = (int)a.Status });
        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Completed,
                It.IsAny<AppointmentCompletedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentCompletedIntegrationEvent, CancellationToken>((_, e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var handler = new CompleteAppointmentCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _appointmentsRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

        var result = await handler.Handle(new CompleteAppointmentCommand(appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.AppointmentSequence.Should().Be(appt.MutationSequence);
    }

    [Fact]
    public async Task Update_Should_Not_Enqueue_When_Terminal_NoOp()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, AppointmentStatus.Completed);
        appt.AdvanceMutationSequence();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt));

        var handler = new UpdateAppointmentCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _appointmentsRead.Object,
            _clinics.Object,
            _pets.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

        var result = await handler.Handle(
            new UpdateAppointmentCommand(appt.Id, cid, pid, appt.ScheduledAtUtc, AppointmentType.Examination, AppointmentStatus.Completed, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.MutationSequence.Should().Be(1);
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupCreateSuccess(Guid tid, Guid cid, Guid pid)
    {
        var when = SlotAlignedUtcPlusDays(1);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
    }

    private void SetupRescheduleSuccess(Guid tid, Appointment appt, DateTime when)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
    }

    private void SetupLifecycleRead(Guid tid, Appointment appt)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
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
}
