using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>
/// Kiracının, verilen andan sonra tamamlanmış başka bir oturumu (BILL-005): bir oturum açıldıktan sonra başka bir ödeme
/// paketi açtıysa iki ödeme aynı denemeye aittir; sonradan doğrulanan ödeme çift ödemedir.
/// </summary>
public sealed class CheckoutSessionCompletedAfterSpec : Specification<BillingCheckoutSession>
{
    public CheckoutSessionCompletedAfterSpec(Guid tenantId, Guid excludeSessionId, DateTime completedAfterUtc)
    {
        Query.AsNoTracking();
        Query.Where(x =>
                x.TenantId == tenantId
                && x.Id != excludeSessionId
                && x.Status == BillingCheckoutSessionStatus.Completed
                && x.CompletedAtUtc > completedAfterUtc)
            .OrderBy(x => x.CompletedAtUtc);
    }
}
