namespace Backend.Veteriner.Application.Reports.Payments.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> üzerinden payment report JSON (GET /api/v1/reports/payments) okuma abstraction (15G).
/// Aggregate'ler (total count, total amount) SQL tarafında hesaplanır; tüm satırlar sırf aggregate için belleğe çekilmez.
/// Items mevcut report JSON davranışındaki sayfalama/sıralama (<c>PaidAtUtc DESC, PaymentId DESC</c>) ile birebir döner.
/// </summary>
public interface IPaymentsReportReadModelReader
{
    Task<PaymentsReportReadResult> GetReportAsync(
        PaymentsReportReadRequest request,
        CancellationToken cancellationToken = default);
}
