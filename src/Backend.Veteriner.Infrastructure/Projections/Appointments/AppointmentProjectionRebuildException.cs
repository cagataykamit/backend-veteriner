namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionRebuildException : InvalidOperationException
{
    public AppointmentProjectionRebuildException(
        string message,
        int pendingAppointmentOutboxCount,
        int deadLetterAppointmentOutboxCount)
        : base(message)
    {
        PendingAppointmentOutboxCount = pendingAppointmentOutboxCount;
        DeadLetterAppointmentOutboxCount = deadLetterAppointmentOutboxCount;
    }

    public int PendingAppointmentOutboxCount { get; }
    public int DeadLetterAppointmentOutboxCount { get; }
}
