using Backend.Veteriner.Application.Projections.Payments;

namespace Backend.Veteriner.Infrastructure.Projections.Payments;

public sealed class PaymentProjectionWorkerIdentity : IPaymentProjectionWorkerIdentity
{
    public PaymentProjectionWorkerIdentity()
    {
        var machine = Environment.MachineName;
        var pid = Environment.ProcessId;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        var workerId = $"{machine}:{pid}:{shortGuid}";

        WorkerId = workerId.Length <= 128 ? workerId : workerId[..128];
    }

    public string WorkerId { get; }
}
