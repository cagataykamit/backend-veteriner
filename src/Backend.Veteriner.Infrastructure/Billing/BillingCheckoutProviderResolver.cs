using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Billing;

public sealed class BillingCheckoutProviderResolver : IBillingCheckoutProviderResolver
{
    private readonly IReadOnlyDictionary<BillingProvider, IBillingCheckoutProvider> _providers;
    private readonly ILogger<BillingCheckoutProviderResolver> _logger;

    public BillingCheckoutProviderResolver(
        IEnumerable<IBillingCheckoutProvider> providers,
        ILogger<BillingCheckoutProviderResolver> logger)
    {
        _providers = providers.ToDictionary(p => p.Provider);
        _logger = logger;
    }

    public IBillingCheckoutProvider Resolve(BillingProvider provider)
    {
        if (_providers.TryGetValue(provider, out var resolved))
            return resolved;

        _logger.LogError(
            "Billing provider {Provider} DI kayıtlarında yok; Manual sağlayıcıya düşülüyor. Bu beklenmeyen bir durumdur.",
            provider);

        return _providers[BillingProvider.Manual];
    }
}
