using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Domain.Appointments;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments;

internal static class AppointmentHandlerOutboxTestSupport
{
    public static void SetupDefaultOutboxMocks(
        Mock<IAppointmentProjectionSnapshotFactory> snapshotFactory,
        Mock<IAppointmentIntegrationEventOutbox> eventOutbox)
    {
        snapshotFactory
            .Setup(f => f.CreateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment a, CancellationToken _) => CreateSnapshot(a));

        snapshotFactory
            .Setup(f => f.CreateScalarsFromPrevious(It.IsAny<Appointment>(), It.IsAny<AppointmentProjectionSnapshot>()))
            .Returns((Appointment a, AppointmentProjectionSnapshot previous) => previous with
            {
                AppointmentId = a.Id,
                TenantId = a.TenantId,
                ClinicId = a.ClinicId,
                PetId = a.PetId,
                ScheduledAtUtc = a.ScheduledAtUtc,
                DurationMinutes = a.DurationMinutes,
                AppointmentType = (int)a.AppointmentType,
                Status = (int)a.Status,
                Notes = a.Notes
            });

        eventOutbox
            .Setup(o => o.EnqueueAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public static AppointmentProjectionSnapshot CreateSnapshot(Appointment appointment)
        => new(
            appointment.Id,
            appointment.TenantId,
            appointment.ClinicId,
            "Test Clinic",
            appointment.PetId,
            "Test Pet",
            Guid.NewGuid(),
            "Dog",
            Guid.NewGuid(),
            "Test Client",
            "+905551112233",
            appointment.ScheduledAtUtc,
            appointment.DurationMinutes,
            (int)appointment.AppointmentType,
            (int)appointment.Status,
            appointment.Notes);
}
