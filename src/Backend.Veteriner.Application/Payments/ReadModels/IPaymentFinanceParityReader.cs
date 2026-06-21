namespace Backend.Veteriner.Application.Payments.ReadModels;

public interface IPaymentFinanceParityReader
{
    Task<PaymentFinanceParityResult> GetGlobalParityAsync(CancellationToken cancellationToken = default);

    Task<PaymentFinanceParityResult> GetTenantParityAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
