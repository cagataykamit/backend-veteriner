using Backend.Veteriner.Application.Appointments.Commands.Cancel;
using Backend.Veteriner.Application.Appointments.Commands.Complete;
using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Appointments.Commands.Reschedule;
using Backend.Veteriner.Application.Appointments.Commands.Update;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using ClinicAppointmentSettingsEntity = Backend.Veteriner.Domain.Clinics.ClinicAppointmentSettings;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.IntegrationEvents;

public sealed class AppointmentCommandHandlerOutboxEmissionTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointmentsRead = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettingsEntity>> _clinicAppointmentSettings = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _clinicWorkingHoursRead = new();
    private readonly Mock<IRepository<Appointment>> _appointmentsWrite = new();
    private readonly Mock<IAppointmentProjectionSnapshotFactory> _snapshotFactory = new();
    private readonly Mock<IAppointmentIntegrationEventOutbox> _eventOutbox = new();

    public AppointmentCommandHandlerOutboxEmissionTests()
    {
        _clinicWorkingHoursRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>());
    }

    [Fact]
    public async Task Create_Should_EnqueueCreatedEvent_WithCurrentSnapshot()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(1);
        Appointment? capturedAppointment = null;
        AppointmentCreatedIntegrationEvent? capturedEvent = null;
        string? capturedType = null;

        SetupCreateSuccess(tid, cid, pid, when);
        _appointmentsWrite.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .Callback<Appointment, CancellationToken>((a, _) => capturedAppointment = a);

        _snapshotFactory.Setup(f => f.CreateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment a, CancellationToken _) => AppointmentHandlerOutboxTestSupport.CreateSnapshot(a));

        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Created,
                It.IsAny<AppointmentCreatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentCreatedIntegrationEvent, CancellationToken>((t, e, _) =>
            {
                capturedType = t;
                capturedEvent = e;
            })
            .Returns(Task.CompletedTask);

        var handler = CreateCreateHandler();
        var result = await handler.Handle(
            new CreateAppointmentCommand(cid, pid, when, AppointmentType.Vaccination, null, "note"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedAppointment.Should().NotBeNull();
        capturedType.Should().Be(AppointmentIntegrationEventTypes.Created);
        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventId.Should().NotBe(Guid.Empty);
        capturedEvent.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        capturedEvent.Current.AppointmentId.Should().Be(capturedAppointment!.Id);
        capturedEvent.Current.Status.Should().Be((int)AppointmentStatus.Scheduled);
        capturedEvent.Current.ScheduledAtUtc.Should().Be(when);

        _eventOutbox.Verify(o => o.EnqueueAsync(
            AppointmentIntegrationEventTypes.Created,
            It.IsAny<AppointmentCreatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _appointmentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_NotEnqueue_When_ValidationFails()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var handler = CreateCreateHandler();

        var result = await handler.Handle(
            new CreateAppointmentCommand(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), AppointmentType.Examination),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_EnqueueSingleUpdatedEvent_WithPreviousAndCurrent()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(2);
        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, null, "old");
        var previous = AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt);
        AppointmentUpdatedIntegrationEvent? captured = null;

        SetupUpdateSuccess(tid, cid, pid, appt, when);
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        _snapshotFactory.Setup(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), previous))
            .Returns((Appointment a, AppointmentProjectionSnapshot _) => previous with
            {
                ScheduledAtUtc = a.ScheduledAtUtc,
                AppointmentType = (int)a.AppointmentType,
                Notes = a.Notes
            });

        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Updated,
                It.IsAny<AppointmentUpdatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentUpdatedIntegrationEvent, CancellationToken>((_, e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateUpdateHandler();
        var result = await handler.Handle(
            new UpdateAppointmentCommand(
                appt.Id,
                cid,
                pid,
                when,
                AppointmentType.Vaccination,
                AppointmentStatus.Scheduled,
                "new"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Previous.Notes.Should().Be("old");
        captured.Current.Notes.Should().Be("new");
        captured.Current.AppointmentType.Should().Be((int)AppointmentType.Vaccination);

        _eventOutbox.Verify(o => o.EnqueueAsync(
            AppointmentIntegrationEventTypes.Updated,
            It.IsAny<AppointmentUpdatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _appointmentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_RebuildCurrentSnapshot_When_ClinicOrPetChanges()
    {
        var tid = Guid.NewGuid();
        var oldCid = Guid.NewGuid();
        var newCid = Guid.NewGuid();
        var oldPid = Guid.NewGuid();
        var newPid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(2);
        var appt = new Appointment(tid, oldCid, oldPid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination);
        var previous = AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt) with { ClinicName = "Old Clinic", PetName = "Old Pet" };
        var rebuilt = previous with { ClinicId = newCid, PetId = newPid, ClinicName = "New Clinic", PetName = "New Pet" };

        SetupUpdateSuccess(tid, newCid, newPid, appt, when);
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        _snapshotFactory.Setup(f => f.CreateAsync(It.Is<Appointment>(a => a.ClinicId == newCid && a.PetId == newPid), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rebuilt);

        AppointmentUpdatedIntegrationEvent? captured = null;
        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Updated,
                It.IsAny<AppointmentUpdatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentUpdatedIntegrationEvent, CancellationToken>((_, e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateUpdateHandler();
        var result = await handler.Handle(
            new UpdateAppointmentCommand(appt.Id, newCid, newPid, when, AppointmentType.Examination, AppointmentStatus.Scheduled, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.Previous.ClinicName.Should().Be("Old Clinic");
        captured.Current.ClinicName.Should().Be("New Clinic");
        captured.Current.PetName.Should().Be("New Pet");
        _snapshotFactory.Verify(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), It.IsAny<AppointmentProjectionSnapshot>()), Times.Never);
    }

    [Fact]
    public async Task Reschedule_Should_EnqueueRescheduledEvent_PreservingDisplayFields()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var oldWhen = SlotAlignedUtcPlusDays(1);
        var newWhen = SlotAlignedUtcPlusDays(3);
        var appt = new Appointment(tid, cid, pid, oldWhen, 30, AppointmentType.Examination);
        var previous = AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt) with { ClinicName = "Clinic A", PetName = "Boncuk" };

        SetupRescheduleSuccess(tid, appt, newWhen);
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        _snapshotFactory.Setup(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), previous))
            .Returns((Appointment a, AppointmentProjectionSnapshot _) => previous with { ScheduledAtUtc = a.ScheduledAtUtc });

        AppointmentRescheduledIntegrationEvent? captured = null;
        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Rescheduled,
                It.IsAny<AppointmentRescheduledIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentRescheduledIntegrationEvent, CancellationToken>((_, e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateRescheduleHandler();
        var result = await handler.Handle(new RescheduleAppointmentCommand(appt.Id, newWhen), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.Previous.ScheduledAtUtc.Should().Be(oldWhen);
        captured.Current.ScheduledAtUtc.Should().Be(newWhen);
        captured.Current.ClinicName.Should().Be("Clinic A");
        captured.Current.PetName.Should().Be("Boncuk");
    }

    [Fact]
    public async Task Cancel_Should_EnqueueCancelledEvent_WithUpdatedNotes()
    {
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination);
        var previous = AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt) with { Status = (int)AppointmentStatus.Scheduled };

        SetupLifecycleRead(tid, appt);
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        _snapshotFactory.Setup(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), previous))
            .Returns((Appointment a, AppointmentProjectionSnapshot _) => previous with
            {
                Status = (int)a.Status,
                Notes = a.Notes
            });

        AppointmentCancelledIntegrationEvent? captured = null;
        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Cancelled,
                It.IsAny<AppointmentCancelledIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentCancelledIntegrationEvent, CancellationToken>((_, e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateCancelHandler();
        var result = await handler.Handle(new CancelAppointmentCommand(appt.Id, "Müşteri aradı"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.Previous.Status.Should().Be((int)AppointmentStatus.Scheduled);
        captured.Current.Status.Should().Be((int)AppointmentStatus.Cancelled);
        captured.Current.Notes.Should().Contain("Müşteri aradı");
    }

    [Fact]
    public async Task Complete_Should_EnqueueCompletedEvent()
    {
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination);
        var previous = AppointmentHandlerOutboxTestSupport.CreateSnapshot(appt) with { Status = (int)AppointmentStatus.Scheduled };

        SetupLifecycleRead(tid, appt);
        _snapshotFactory.Setup(f => f.CreateAsync(appt, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        _snapshotFactory.Setup(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), previous))
            .Returns((Appointment a, AppointmentProjectionSnapshot _) => previous with { Status = (int)a.Status });

        AppointmentCompletedIntegrationEvent? captured = null;
        _eventOutbox.Setup(o => o.EnqueueAsync(
                AppointmentIntegrationEventTypes.Completed,
                It.IsAny<AppointmentCompletedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AppointmentCompletedIntegrationEvent, CancellationToken>((_, e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateCompleteHandler();
        var result = await handler.Handle(new CompleteAppointmentCommand(appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.Previous.Status.Should().Be((int)AppointmentStatus.Scheduled);
        captured.Current.Status.Should().Be((int)AppointmentStatus.Completed);
    }

    private CreateAppointmentCommandHandler CreateCreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _appointmentsRead.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    private UpdateAppointmentCommandHandler CreateUpdateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _appointmentsRead.Object,
            _clinics.Object,
            _pets.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    private RescheduleAppointmentCommandHandler CreateRescheduleHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _appointmentsRead.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    private CancelAppointmentCommandHandler CreateCancelHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _appointmentsRead.Object, _appointmentsWrite.Object, _snapshotFactory.Object, _eventOutbox.Object);

    private CompleteAppointmentCommandHandler CreateCompleteHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _appointmentsRead.Object, _appointmentsWrite.Object, _snapshotFactory.Object, _eventOutbox.Object);

    private void SetupCreateSuccess(Guid tid, Guid cid, Guid pid, DateTime when)
    {
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

    private void SetupUpdateSuccess(Guid tid, Guid cid, Guid pid, Appointment appt, DateTime when)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
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
