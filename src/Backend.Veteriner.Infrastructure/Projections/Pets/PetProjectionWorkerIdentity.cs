using Backend.Veteriner.Application.Projections.Pets;

namespace Backend.Veteriner.Infrastructure.Projections.Pets;

public sealed class PetProjectionWorkerIdentity : IPetProjectionWorkerIdentity
{
    public PetProjectionWorkerIdentity()
    {
        var machine = Environment.MachineName;
        var pid = Environment.ProcessId;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        var workerId = $"{machine}:{pid}:{shortGuid}";

        WorkerId = workerId.Length <= 128 ? workerId : workerId[..128];
    }

    public string WorkerId { get; }
}
