namespace Backend.Veteriner.Application.Projections.Appointments;

/// <summary>
/// Process başına sabit worker kimliği (max 128 karakter; tenant/clinic/user/appointment içermez).
/// </summary>
public interface IAppointmentProjectionWorkerIdentity
{
    string WorkerId { get; }
}
