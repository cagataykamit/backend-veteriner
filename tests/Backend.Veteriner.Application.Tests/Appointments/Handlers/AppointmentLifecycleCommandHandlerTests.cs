using Backend.Veteriner.Application.Appointments.Commands.Cancel;
using Backend.Veteriner.Application.Appointments.Commands.Complete;
using Backend.Veteriner.Application.Appointments.Commands.Reschedule;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class AppointmentLifecycleCommandHandlerTests
{
    private readonly Mock<IReadRepository<Appointment>> _read = new();
    private readonly Mock<IRepository<Appointment>> _write = new();

    [Fact]
    public async Task Cancel_Should_Fail_When_NotFound()
    {
        var handler = new CancelAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var aid = Guid.NewGuid();

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await handler.Handle(new CancelAppointmentCommand(tid, aid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotFound");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Should_Fail_When_NotScheduled()
    {
        var handler = new CancelAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);
        _ = appt.Complete();

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(new CancelAppointmentCommand(tid, appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Should_Succeed_When_Scheduled()
    {
        var handler = new CancelAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(new CancelAppointmentCommand(tid, appt.Id, "Müşteri aradı"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.Cancelled);
        appt.Notes.Should().Contain("İptal:");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Complete_Should_Fail_When_NotScheduled()
    {
        var handler = new CompleteAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);
        _ = appt.Cancel();

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(new CompleteAppointmentCommand(tid, appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
    }

    [Fact]
    public async Task Complete_Should_Succeed_When_Scheduled()
    {
        var handler = new CompleteAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(new CompleteAppointmentCommand(tid, appt.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.Completed);
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reschedule_Should_Fail_When_ClinicSlotDuplicate()
    {
        var handler = new RescheduleAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var when = DateTime.UtcNow.AddDays(3);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentScheduledSlotAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Appointment(tid, appt.ClinicId, Guid.NewGuid(), when, null));

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(tid, appt.Id, when),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ClinicSlotDuplicate");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reschedule_Should_Succeed_When_NoConflict()
    {
        var handler = new RescheduleAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var when = DateTime.UtcNow.AddDays(5);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentScheduledSlotAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentScheduledSlotForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(tid, appt.Id, when),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.ScheduledAtUtc.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reschedule_Should_Fail_When_NotScheduled()
    {
        var handler = new RescheduleAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var when = DateTime.UtcNow.AddDays(3);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);
        _ = appt.Complete();

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(tid, appt.Id, when),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        _write.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reschedule_Should_Fail_When_TooFarInPast()
    {
        var handler = new RescheduleAppointmentCommandHandler(_read.Object, _write.Object);
        var tid = Guid.NewGuid();
        var past = DateTime.UtcNow.AddDays(-30);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(tid, appt.Id, past),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ScheduledTooFarInPast");
    }
}
