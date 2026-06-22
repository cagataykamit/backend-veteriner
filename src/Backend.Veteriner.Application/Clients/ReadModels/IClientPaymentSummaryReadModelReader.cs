namespace Backend.Veteriner.Application.Clients.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> üzerinden client payment summary aggregate + recent payments okuma abstraction (15E).
/// Aggregate'ler (count, currency totals, last payment date) SQL tarafında hesaplanır; tüm ödeme satırları belleğe çekilmez.
/// </summary>
public interface IClientPaymentSummaryReadModelReader
{
    Task<ClientPaymentSummaryReadResult> GetSummaryAsync(
        ClientPaymentSummaryReadRequest request,
        CancellationToken cancellationToken = default);
}
