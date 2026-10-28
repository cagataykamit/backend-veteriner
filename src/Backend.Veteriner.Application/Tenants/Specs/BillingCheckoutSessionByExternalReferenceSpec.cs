using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>Sağlayıcı referansıyla oturum (iyzico'da ödeme formu token'ı; dönüş isteği yalnız token taşıyabilir).</summary>
public sealed class BillingCheckoutSessionByExternalReferenceSpec : Specification<BillingCheckoutSession>
{
    public BillingCheckoutSessionByExternalReferenceSpec(string externalReference)
    {
        Query.Where(x => x.ExternalReference == externalReference);
    }
}
