using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

public interface IBillingWebhookSignatureVerifier
{
    Result Verify(BillingProvider provider, string rawBody, IReadOnlyDictionary<string, string> headers);
}
