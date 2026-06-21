namespace Backend.Veteriner.Application.Payments.ReadModels;

public interface IPaymentsListReadModelReader
{
    Task<PaymentsListReadResult> GetListAsync(
        PaymentsListReadRequest request,
        CancellationToken cancellationToken = default);
}
