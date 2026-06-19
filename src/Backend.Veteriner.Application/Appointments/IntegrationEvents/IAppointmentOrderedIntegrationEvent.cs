namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

/// <summary>
/// Appointment integration event'lerinde güvenilir sıralama metadata'sı.
/// </summary>
public interface IAppointmentOrderedIntegrationEvent
{
    Guid AppointmentId { get; }

    long AppointmentSequence { get; }
}
