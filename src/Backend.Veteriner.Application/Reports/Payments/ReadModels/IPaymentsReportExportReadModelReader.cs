namespace Backend.Veteriner.Application.Reports.Payments.ReadModels;

/// <summary>
/// Query DB <c>PaymentReadModels</c> üzerinden payment export CSV/XLSX okuma abstraction (15J).
/// Tüm eşleşen satırlar <c>PaidAtUtc DESC, PaymentId DESC</c> sırasıyla döner (sayfalama yok).
/// Aggregate yalnızca <see cref="PaymentsReportExportReadResult.TotalCount"/> için SQL <c>COUNT(*)</c> kullanılır;
/// limit aşımında items belleğe çekilmez (handler/pipeline count doğrulaması sonrası).
/// </summary>
public interface IPaymentsReportExportReadModelReader
{
    Task<PaymentsReportExportReadResult> GetExportAsync(
        PaymentsReportExportReadRequest request,
        CancellationToken cancellationToken = default);
}
