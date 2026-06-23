using Backend.Veteriner.Application.Payments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Payments.ReadModels;

public interface IPaymentGetByIdReadModelReader
{
    Task<PaymentDetailDto?> GetByIdAsync(
        Guid tenantId,
        Guid paymentId,
        CancellationToken cancellationToken = default);
}
