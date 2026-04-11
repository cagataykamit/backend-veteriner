using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

public interface IBillingWebhookPayloadParser
{
    Result<BillingWebhookNormalizedEvent> Parse(BillingProvider provider, string rawBody);
}
