using Backend.Veteriner.Application.Appointments.IntegrationEvents;

namespace Backend.Veteriner.Application.Projections.Appointments;

/// <summary>
/// Düşük cardinality metric tag değerleri için appointment outbox event type eşlemesi.
/// </summary>
public static class AppointmentProjectionEventTypeTags
{
    public const string Unknown = "unknown";

    public const string EventCreated = "created";
    public const string EventRescheduled = "rescheduled";
    public const string EventCancelled = "cancelled";

    public const string OperationCreate = "create";
    public const string OperationReschedule = "reschedule";
    public const string OperationCancel = "cancel";

    public static string MapEventType(string? outboxType)
        => outboxType switch
        {
            AppointmentIntegrationEventTypes.Created => EventCreated,
            AppointmentIntegrationEventTypes.Rescheduled => EventRescheduled,
            AppointmentIntegrationEventTypes.Cancelled => EventCancelled,
            _ => Unknown
        };

    public static string MapOperation(string? outboxType)
        => outboxType switch
        {
            AppointmentIntegrationEventTypes.Created => OperationCreate,
            AppointmentIntegrationEventTypes.Rescheduled => OperationReschedule,
            AppointmentIntegrationEventTypes.Cancelled => OperationCancel,
            _ => Unknown
        };
}
