namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

/// <summary>
/// Command DB outbox'taki appointment integration eventlerini Query DB read-model'lerine uygular.
/// </summary>
public interface IAppointmentProjectionProcessor
{
    /// <summary>
    /// Hazır appointment outbox mesajlarını işler; işlenen mesaj sayısını döner.
    /// </summary>
    Task<int> ProcessBatchAsync(CancellationToken cancellationToken);
}
