using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed record PaymentPaidAtAmountRow(DateTime PaidAtUtc, decimal Amount);

/// <summary>
/// Ödeme tutarı + ödeme zamanı alanlarını döndürür; pencere [start, end) — <see cref="Payment.PaidAtUtc"/>.
/// Dashboard finans özetinde farklı zaman pencerelerini tek sorgu ile hesaplamak için kullanılır.
/// </summary>
public sealed class PaymentsPaidAtAmountInWindowSpec : Specification<Payment, PaymentPaidAtAmountRow>
{
    public PaymentsPaidAtAmountInWindowSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime startUtcInclusive,
        DateTime endUtcExclusive)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId
            && p.PaidAtUtc >= startUtcInclusive
            && p.PaidAtUtc < endUtcExclusive);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        Query.Select(p => new PaymentPaidAtAmountRow(p.PaidAtUtc, p.Amount));
    }
}
