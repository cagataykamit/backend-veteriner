using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

/// <summary>Ödeme tutarları (yalnızca <see cref="Payment.Amount"/>); pencere [start, end) — <see cref="Payment.PaidAtUtc"/>.</summary>
public sealed class PaymentsAmountInPaidAtWindowSpec : Specification<Payment, decimal>
{
    public PaymentsAmountInPaidAtWindowSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime startUtcInclusive,
        DateTime endUtcExclusive)
    {
        Query.Where(p => p.TenantId == tenantId
            && p.PaidAtUtc >= startUtcInclusive
            && p.PaidAtUtc < endUtcExclusive);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        Query.Select(p => p.Amount);
    }
}
