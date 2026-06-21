namespace Backend.Veteriner.Application.Projections.Payments;

public interface IPaymentProjectionWorkerIdentity
{
    string WorkerId { get; }
}
