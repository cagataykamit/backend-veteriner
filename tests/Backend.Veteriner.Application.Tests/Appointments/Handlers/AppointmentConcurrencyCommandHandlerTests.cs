using Backend.Veteriner.Application.Appointments.Commands.Reschedule;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class AppointmentConcurrencyCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Appointment>> _appointmentsRead = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettings>> _clinicAppointmentSettings = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _clinicWorkingHoursRead = new();
    private readonly Mock<IRepository<Appointment>> _appointmentsWrite = new();
    private readonly Mock<IAppointmentProjectionSnapshotFactory> _snapshotFactory = new();
    private readonly Mock<IAppointmentIntegrationEventOutbox> _eventOutbox = new();

    public AppointmentConcurrencyCommandHandlerTests()
    {
        _clinicWorkingHoursRead
            .Setup(r => r.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>());

        AppointmentHandlerOutboxTestSupport.SetupDefaultOutboxMocks(_snapshotFactory, _eventOutbox);
    }

    [Fact]
    public async Task Reschedule_Should_Map_DbUpdateConcurrencyException_To_Appointments_ConcurrencyConflict()
    {
        var tid = Guid.NewGuid();
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), SlotAlignedUtcPlusDays(1));
        appt.AdvanceMutationSequence();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingAtClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentOverlappingForPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        _appointmentsWrite.Setup(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict.", innerException: null));

        var handler = new RescheduleAppointmentCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _appointmentsRead.Object,
            _clinicAppointmentSettings.Object,
            _clinicWorkingHoursRead.Object,
            _appointmentsWrite.Object,
            _snapshotFactory.Object,
            _eventOutbox.Object);

        var result = await handler.Handle(
            new RescheduleAppointmentCommand(appt.Id, SlotAlignedUtcPlusDays(3)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Appointments.ConcurrencyConflict");
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
