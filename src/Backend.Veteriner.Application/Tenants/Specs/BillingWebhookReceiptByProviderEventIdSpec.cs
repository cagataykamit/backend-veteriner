using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class BillingWebhookReceiptByProviderEventIdSpec : Specification<BillingWebhookReceipt>
{
    public BillingWebhookReceiptByProviderEventIdSpec(BillingProvider provider, string providerEventId)
    {
        Query.Where(x => x.Provider == provider && x.ProviderEventId == providerEventId);
    }
}
