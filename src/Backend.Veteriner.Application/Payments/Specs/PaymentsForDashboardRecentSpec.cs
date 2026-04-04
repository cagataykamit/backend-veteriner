using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed record DashboardFinancePaymentRow(
    Guid Id,
    DateTime PaidAtUtc,
    Guid ClientId,
    Guid ClinicId,
    Guid? PetId,
    decimal Amount,
    string Currency,
    PaymentMethod Method);

public sealed class PaymentsForDashboardRecentSpec : Specification<Payment, DashboardFinancePaymentRow>
{
    public PaymentsForDashboardRecentSpec(Guid tenantId, Guid? clinicId, int take)
    {
        Query.Where(p => p.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        Query.OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(take);
        Query.Select(p => new DashboardFinancePaymentRow(
            p.Id,
            p.PaidAtUtc,
            p.ClientId,
            p.ClinicId,
            p.PetId,
            p.Amount,
            p.Currency,
            p.Method));
    }
}
