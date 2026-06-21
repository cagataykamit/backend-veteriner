namespace Backend.Veteriner.Application.Projections.Payments;

public interface IPaymentProjectionStatusReader
{
    Task<PaymentProjectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
