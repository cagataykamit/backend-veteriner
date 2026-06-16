using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

/// <summary>
/// Appointment integration event payload'ları için Command DB'den denormalize snapshot üretir.
/// </summary>
public interface IAppointmentProjectionSnapshotFactory
{
    /// <summary>
    /// Appointment scalar değerlerini verilen entity'den; clinic/pet/client/species alanlarını Command DB'den okur.
    /// </summary>
    Task<AppointmentProjectionSnapshot> CreateAsync(
        Appointment appointment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clinic/pet/client/species display alanlarını koruyarak appointment'ın güncel scalar değerlerini yansıtır.
    /// </summary>
    AppointmentProjectionSnapshot CreateScalarsFromPrevious(
        Appointment appointment,
        AppointmentProjectionSnapshot previous);
}
