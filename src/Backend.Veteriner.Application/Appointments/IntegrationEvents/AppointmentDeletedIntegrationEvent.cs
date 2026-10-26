namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

/// <summary>
/// Randevu kalıcı olarak silindiğinde üretilir (yalnız örnek klinik temizliği; EXAMPLE_CLINIC_API_CONTRACT.md §10).
/// Projeksiyon read-model satırını kaldırır, <see cref="Previous"/> anlık görüntüsünün gününü ve hasta/müşteri son
/// ziyaretini yeniden hesaplar.
/// </summary>
public sealed record AppointmentDeletedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    long AppointmentSequence,
    AppointmentProjectionSnapshot Previous) : IAppointmentOrderedIntegrationEvent
{
    public Guid AppointmentId => Previous.AppointmentId;
}
