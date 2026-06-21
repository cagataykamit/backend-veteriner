using Backend.Veteriner.Application.Projections.Clients;

namespace Backend.Veteriner.Infrastructure.Projections.Clients;

public sealed class ClientProjectionWorkerIdentity : IClientProjectionWorkerIdentity
{
    public ClientProjectionWorkerIdentity()
    {
        var machine = Environment.MachineName;
        var pid = Environment.ProcessId;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        var workerId = $"{machine}:{pid}:{shortGuid}";

        WorkerId = workerId.Length <= 128 ? workerId : workerId[..128];
    }

    public string WorkerId { get; }
}
