using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Tenants.Billing;

public sealed class CheckoutPaymentReconciler : ICheckoutPaymentReconciler
{
    private readonly IBillingCheckoutProviderResolver _providers;
    private readonly ISubscriptionCheckoutActivationService _activation;
    private readonly ILogger<CheckoutPaymentReconciler> _logger;

    public CheckoutPaymentReconciler(
        IBillingCheckoutProviderResolver providers,
        ISubscriptionCheckoutActivationService activation,
        ILogger<CheckoutPaymentReconciler> logger)
    {
        _providers = providers;
        _activation = activation;
        _logger = logger;
    }

    public async Task<CheckoutPageState> ReconcileAsync(BillingCheckoutSession session, Guid? tenantIdConstraint, CancellationToken ct = default)
    {
        var state = await _providers.Resolve(session.Provider).GetCheckoutPageStateAsync(session, ct);
        if (state != CheckoutPageState.Paid)
            return state;

        var activation = await _activation.TryActivateAsync(
            session.Id,
            tenantIdConstraint,
            session.Provider,
            externalReference: null,
            BillingActivationSource.ProviderVerified,
            ct);
        if (!activation.IsSuccess)
        {
            _logger.LogWarning(
                "Sağlayıcıda ödemesi alınmış oturum etkinleştirilemedi: {Code} {Message} SessionId={SessionId}",
                activation.Error.Code,
                activation.Error.Message,
                session.Id);
        }

        return state;
    }
}
