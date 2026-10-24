using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Application.Common.Specifications;

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
    public PaymentsForDashboardRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
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
