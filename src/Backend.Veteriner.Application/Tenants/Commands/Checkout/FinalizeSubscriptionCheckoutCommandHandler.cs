using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed class FinalizeSubscriptionCheckoutCommandHandler
    : IRequestHandler<FinalizeSubscriptionCheckoutCommand, Result<SubscriptionCheckoutSessionDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ISubscriptionCheckoutActivationService _activation;

    public FinalizeSubscriptionCheckoutCommandHandler(
        ITenantContext tenantContext,
        ISubscriptionCheckoutActivationService activation)
    {
        _tenantContext = tenantContext;
        _activation = activation;
    }

    public async Task<Result<SubscriptionCheckoutSessionDto>> Handle(FinalizeSubscriptionCheckoutCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Tenants.AccessDenied",
                "Bu kiracının checkout oturumunu finalize etme yetkisi yok.");
        }

        return await _activation.TryActivateAsync(
            request.CheckoutSessionId,
            request.TenantId,
            providerMustMatch: null,
            request.ExternalReference,
            BillingActivationSource.Manual,
            ct);
    }
}

