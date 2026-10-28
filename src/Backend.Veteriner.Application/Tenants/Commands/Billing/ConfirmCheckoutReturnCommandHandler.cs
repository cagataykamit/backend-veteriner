using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.Billing;

public sealed class ConfirmCheckoutReturnCommandHandler : IRequestHandler<ConfirmCheckoutReturnCommand, Result<Guid?>>
{
    private readonly IReadRepository<BillingCheckoutSession> _sessionsRead;
    private readonly ICheckoutPaymentReconciler _reconciler;
    private readonly ITenantScope _tenantScope;
    private readonly TimeProvider _timeProvider;

    public ConfirmCheckoutReturnCommandHandler(
        IReadRepository<BillingCheckoutSession> sessionsRead,
        ICheckoutPaymentReconciler reconciler,
        ITenantScope tenantScope,
        TimeProvider timeProvider)
    {
        _sessionsRead = sessionsRead;
        _reconciler = reconciler;
        _tenantScope = tenantScope;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid?>> Handle(ConfirmCheckoutReturnCommand request, CancellationToken ct)
    {
        var token = request.ProviderToken?.Trim();
        if (request.CheckoutSessionId is null && string.IsNullOrEmpty(token))
            return Result<Guid?>.Success(null);

        // Dönüş isteğinde kiracı yoktur: oturum kimlik ya da token ile kiracılar arası aranır, uzlaştırma oturumun
        // kiracısında yapılır (ADR-040).
        BillingCheckoutSession? session;
        using (_tenantScope.AsSystem())
        {
            session = request.CheckoutSessionId is { } id
                ? await _sessionsRead.FirstOrDefaultAsync(new BillingCheckoutSessionByIdSpec(id), ct)
                : await _sessionsRead.FirstOrDefaultAsync(new BillingCheckoutSessionByExternalReferenceSpec(token!), ct);
        }

        if (session is null)
            return Result<Guid?>.Success(null);

        if (session.IsOpen(_timeProvider.GetUtcNow().UtcDateTime))
        {
            using var tenantScope = _tenantScope.UseTenant(session.TenantId);
            await _reconciler.ReconcileAsync(session, tenantIdConstraint: null, ct);
        }

        return Result<Guid?>.Success(session.Id);
    }
}
