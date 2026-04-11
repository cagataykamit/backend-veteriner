using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

public interface IBillingCheckoutProviderResolver
{
    IBillingCheckoutProvider Resolve(BillingProvider provider);
}
