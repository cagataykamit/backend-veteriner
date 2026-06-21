using Backend.Veteriner.Application.Projections.Payments;

namespace Backend.Veteriner.Infrastructure.Projections.Payments;

/// <summary>
/// Payment list read-model (PaymentReadModels) count drift sinyalini Command DB + Query DB satır sayılarından derler
/// (CQRS-14F). Salt okunur. Query DB erişilemiyorsa çağrılmamalıdır (caller reachability'yi önceden doğrular).
/// </summary>
public interface IPaymentReadModelHealthReader
{
    Task<PaymentReadModelHealthSignal> GetSignalAsync(CancellationToken cancellationToken = default);
}
