using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Appointments.Commands.Update;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class UpdateAppointmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Appointment>> _appointmentsRead = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettings>> _clinicAppointmentSettings = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _clinicWorkingHoursRead = new();
    private readonly Mock<IRepository<Appointment>> _appointmentsWrite = new();

    private UpdateAppointmentCommandHandler CreateHandler()
    {
        _clinicWorkingHoursRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>());

        return new(
            _tenantContext.Object,
            _clinicContext.Object,
            _appointmentsRead.Object,
            _clinics.Object,
            _pets.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object);
    }

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

    // Working-hours / slot senaryoları sabit 2026-06-01 (yerel Pazartesi) tarihine bağlıydı;
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
    public async Task Handle_Should_Update_AppointmentType_When_Scheduled()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(2);

        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, null, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var cmd = new UpdateAppointmentCommand(aid, cid, pid, when, AppointmentType.Surgery, AppointmentStatus.Scheduled, null);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.AppointmentType.Should().Be(AppointmentType.Surgery);
        appt.ScheduledAtUtc.Should().BeCloseTo(when, TimeSpan.FromSeconds(2));
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Set_Completed_When_Requested()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(2);

        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, null, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var cmd = new UpdateAppointmentCommand(aid, cid, pid, when, AppointmentType.Examination, AppointmentStatus.Completed, null);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.Completed);
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_StatusInvalid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, null, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var cmd = new UpdateAppointmentCommand(
            aid,
            cid,
            pid,
            SlotAlignedUtcPlusDays(2),
            AppointmentType.Examination,
            (AppointmentStatus)99,
            null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.Validation");
    }

    [Fact]
    public async Task Handle_Should_NoOp_When_Already_Completed_And_Same_Status()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, AppointmentStatus.Completed, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var cmd = new UpdateAppointmentCommand(aid, cid, pid, appt.ScheduledAtUtc, AppointmentType.Examination, AppointmentStatus.Completed, null);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Completed_To_Scheduled()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, AppointmentStatus.Completed, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var cmd = new UpdateAppointmentCommand(aid, cid, pid, SlotAlignedUtcPlusDays(3), AppointmentType.Surgery, AppointmentStatus.Scheduled, null);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
    }

    [Fact]
    public async Task Handle_Should_UpdateDuration_When_DurationMinutes_Provided()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(2);

        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, null, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var cmd = new UpdateAppointmentCommand(aid, cid, pid, when, AppointmentType.Surgery, AppointmentStatus.Scheduled, null, DurationMinutes: 120);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.DurationMinutes.Should().Be(120);
        appt.ScheduledEndUtc.Should().BeCloseTo(when.AddMinutes(120), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Handle_Should_PreserveDuration_When_DurationMinutes_Null()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var when = SlotAlignedUtcPlusDays(2);

        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 45, AppointmentType.Examination, null, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var cmd = new UpdateAppointmentCommand(aid, cid, pid, when, AppointmentType.Surgery, AppointmentStatus.Scheduled, null, DurationMinutes: null);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.DurationMinutes.Should().Be(45);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_NewTime_OutsideWorkingHours()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var scheduledUtc = MondayUtc(15, 30); // 18:30 local

        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, null, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
        _clinicWorkingHoursRead.Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>
            {
                ClinicWorkingHour.Create(tid, cid, DayOfWeek.Monday, false, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null)
            });

        var cmd = new UpdateAppointmentCommand(aid, cid, pid, scheduledUtc, AppointmentType.Examination, AppointmentStatus.Scheduled, null, DurationMinutes: 30);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.OutsideWorkingHours");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_NewTime_NotAlignedToSlotInterval()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var scheduledUtc = MondayUtc(6, 10); // 09:10 local

        var appt = new Appointment(tid, cid, pid, SlotAlignedUtcPlusDays(1), 30, AppointmentType.Examination, null, null);
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, aid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(appt);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
        _clinicAppointmentSettings.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicAppointmentSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAppointmentSettings?)null);

        var cmd = new UpdateAppointmentCommand(aid, cid, pid, scheduledUtc, AppointmentType.Examination, AppointmentStatus.Scheduled, null, DurationMinutes: 30);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotAlignedToSlotInterval");
    }
}
