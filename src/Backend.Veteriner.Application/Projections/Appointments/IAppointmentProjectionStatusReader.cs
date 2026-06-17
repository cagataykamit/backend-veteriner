namespace Backend.Veteriner.Application.Projections.Appointments;

public interface IAppointmentProjectionStatusReader
{
    Task<AppointmentProjectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
