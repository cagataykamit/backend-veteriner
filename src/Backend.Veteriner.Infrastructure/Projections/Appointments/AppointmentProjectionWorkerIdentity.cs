using Backend.Veteriner.Application.Projections.Appointments;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionWorkerIdentity : IAppointmentProjectionWorkerIdentity
{
    public AppointmentProjectionWorkerIdentity()
    {
        var machine = Environment.MachineName;
        var pid = Environment.ProcessId;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        var workerId = $"{machine}:{pid}:{shortGuid}";

        WorkerId = workerId.Length <= 128 ? workerId : workerId[..128];
    }

    public string WorkerId { get; }
}
