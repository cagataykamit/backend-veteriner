using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed class CancelSubscriptionCheckoutCommandHandler : IRequestHandler<CancelSubscriptionCheckoutCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<BillingCheckoutSession> _sessionsWrite;
    private readonly IReadRepository<BillingCheckoutSession> _sessionsRead;
    private readonly TimeProvider _timeProvider;

    public CancelSubscriptionCheckoutCommandHandler(
        ITenantContext tenantContext,
        IRepository<BillingCheckoutSession> sessionsWrite,
        IReadRepository<BillingCheckoutSession> sessionsRead,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _sessionsWrite = sessionsWrite;
        _sessionsRead = sessionsRead;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(CancelSubscriptionCheckoutCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
            return Result.Failure("Tenants.AccessDenied", "Bu kiracının checkout oturumunu iptal etme yetkisi yok.");

        var session = await _sessionsRead.FirstOrDefaultAsync(
            new BillingCheckoutSessionByTenantAndIdSpec(request.TenantId, request.CheckoutSessionId), ct);
        if (session is null)
            return Result.Failure("Subscriptions.CheckoutSessionNotFound", "Checkout session bulunamadı.");

        if (session.Status is not (BillingCheckoutSessionStatus.Pending or BillingCheckoutSessionStatus.RedirectReady))
            return Result.Success();

        session.MarkCancelled(_timeProvider.GetUtcNow().UtcDateTime);
        await _sessionsWrite.UpdateAsync(session, ct);
        await _sessionsWrite.SaveChangesAsync(ct);

        return Result.Success();
    }
}
