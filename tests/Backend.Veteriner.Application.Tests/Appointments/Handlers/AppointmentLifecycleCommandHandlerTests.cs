using Backend.Veteriner.Application.Appointments.Commands.Cancel;
using Backend.Veteriner.Application.Appointments.Commands.Complete;
using Backend.Veteriner.Application.Appointments.Commands.Reschedule;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class AppointmentLifecycleCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Appointment>> _read = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettings>> _clinicAppointmentSettings = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _clinicWorkingHoursRead = new();
    private readonly Mock<IRepository<Appointment>> _write = new();
    private readonly Mock<IAppointmentProjectionSnapshotFactory> _snapshotFactory = new();
    private readonly Mock<IAppointmentIntegrationEventOutbox> _eventOutbox = new();

    public AppointmentLifecycleCommandHandlerTests()
    {
        _clinicWorkingHoursRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>());

        AppointmentHandlerOutboxTestSupport.SetupDefaultOutboxMocks(_snapshotFactory, _eventOutbox);
    }

    private CancelAppointmentCommandHandler CreateCancelHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _read.Object, _write.Object, _snapshotFactory.Object, _eventOutbox.Object);

    private CompleteAppointmentCommandHandler CreateCompleteHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _read.Object, _write.Object, _snapshotFactory.Object, _eventOutbox.Object);

    private RescheduleAppointmentCommandHandler CreateRescheduleHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _read.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _write.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

    // Faz 4B-7: Hafta sonuna denk gelen ileri tarihler için Pazartesi'ye kaydır.
    // Default working-hours fallback'inde Pazar kapalı ve Cumartesi sınırlı; testler haftanın gününe bağımlı kalmasın.
    // Negatif (geçmiş) gün ofsetlerinde mevcut davranış korunur (geçmiş tarih senaryoları working-hours kontrolünden önce dönmeli).
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

    // Reschedule working-hours / slot senaryoları sabit 2026-06-01 (yerel Pazartesi) tarihine bağlıydı;
    // güncel tarihe göre geçmişte kaldığında handler önce ScheduledTooFarInPast döndüğü için testler kırılıyordu.
    // Aynı "yerel Pazartesi + sabit UTC saat" semantiğini koruyarak GELECEKTEKİ Pazartesi'yi üretir.
    private static DateTime MondayUtc(int hour, int minute, int second = 0)
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0)
            daysUntilMonday = 7;
        var monday = today.AddDays(daysUntilMonday);
        return new DateTime(monday.Year, monday.Month, monday.Day, hour, minute, second, DateTimeKind.Utc);
    }

    [Fact]
    public async Task Cancel_Should_Fail_When_ContextMissing()
    {
        var handler = CreateCancelHandler();
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await handler.Handle(new CancelAppointmentCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Cancel_Should_Fail_When_NotFound()
    {
        var handler = CreateCancelHandler();
        var tid = Guid.NewGuid();
        var aid = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await handler.Handle(new CancelAppointmentCommand(aid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotFound");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Should_Fail_When_NotScheduled()
    {
        var handler = CreateCancelHandler();
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);
        _ = appt.Complete();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(new CancelAppointmentCommand(appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Should_Succeed_When_Scheduled()
    {
        var handler = CreateCancelHandler();
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(new CancelAppointmentCommand(appt.Id, "Müşteri aradı"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.Cancelled);
        appt.Notes.Should().Contain("İptal:");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Complete_Should_Fail_When_NotScheduled()
    {
        var handler = CreateCompleteHandler();
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);
        _ = appt.Cancel();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(new CompleteAppointmentCommand(appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
    }

    [Fact]
    public async Task Complete_Should_Succeed_When_Scheduled()
    {
        var handler = CreateCompleteHandler();
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(new CompleteAppointmentCommand(appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.Completed);
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reschedule_Should_Fail_When_ClinicSlotDuplicate()
    {
        var handler = CreateRescheduleHandler();
        var tid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(3);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinicAppointmentSettings.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicAppointmentSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAppointmentSettings?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Appointment(tid, appt.ClinicId, Guid.NewGuid(), when, 30, AppointmentType.Other, null, null));

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(appt.Id, when),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ClinicTimeConflict");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reschedule_Should_Succeed_When_NoConflict()
    {
        var handler = CreateRescheduleHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var when = MondayUtc(6, 0); // 09:00 local
        var appt = new Appointment(tid, cid, Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinicAppointmentSettings.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicAppointmentSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAppointmentSettings?)null);
        _clinicWorkingHoursRead.Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>
            {
                ClinicWorkingHour.Create(tid, cid, DayOfWeek.Monday, false, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null)
            });
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(appt.Id, when),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.ScheduledAtUtc.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
        appt.DurationMinutes.Should().Be(30);
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reschedule_Should_Fail_When_NotScheduled()
    {
        var handler = CreateRescheduleHandler();
        var tid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(3);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);
        _ = appt.Complete();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(appt.Id, when),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reschedule_Should_Fail_When_TooFarInPast()
    {
        var handler = CreateRescheduleHandler();
        var tid = Guid.NewGuid();
        var past = DateTime.UtcNow.AddDays(-30);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(appt.Id, past),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ScheduledTooFarInPast");
    }

    [Fact]
    public async Task Reschedule_Should_Fail_When_OutsideWorkingHours()
    {
        var handler = CreateRescheduleHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var when = MondayUtc(15, 30); // 18:30 local
        var appt = new Appointment(tid, cid, Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinicWorkingHoursRead.Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>
            {
                ClinicWorkingHour.Create(tid, cid, DayOfWeek.Monday, false, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null)
            });

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(appt.Id, when),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.OutsideWorkingHours");
    }

    [Fact]
    public async Task Reschedule_Should_Fail_When_NotAlignedToSlotInterval()
    {
        var handler = CreateRescheduleHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var when = MondayUtc(6, 10); // 09:10 local
        var appt = new Appointment(tid, cid, Guid.NewGuid(), SlotAlignedUtcPlusDays(1), 30, AppointmentType.Other, null, null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinicAppointmentSettings.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicAppointmentSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAppointmentSettings?)null);

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(appt.Id, when),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotAlignedToSlotInterval");
    }
}
