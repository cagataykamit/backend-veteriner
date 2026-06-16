namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

/// <summary>
/// Appointment integration event outbox mesaj tipleri (OutboxMessages.Type).
/// Tüm değerler <c>nvarchar(64)</c> sınırına uyar; CLR FullName kullanılmaz.
/// </summary>
public static class AppointmentIntegrationEventTypes
{
    public const string Created = "appointment.created.v1";
    public const string Updated = "appointment.updated.v1";
    public const string Rescheduled = "appointment.rescheduled.v1";
    public const string Cancelled = "appointment.cancelled.v1";
    public const string Completed = "appointment.completed.v1";

    public const int MaxTypeLength = 64;

    public static IReadOnlyList<string> All { get; } =
    [
        Created,
        Updated,
        Rescheduled,
        Cancelled,
        Completed
    ];

    public static bool IsKnown(string eventType)
        => All.Contains(eventType, StringComparer.Ordinal);
}
