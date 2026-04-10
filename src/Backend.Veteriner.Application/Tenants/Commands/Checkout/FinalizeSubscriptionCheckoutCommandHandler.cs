using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed class FinalizeSubscriptionCheckoutCommandHandler
    : IRequestHandler<FinalizeSubscriptionCheckoutCommand, Result<SubscriptionCheckoutSessionDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<TenantSubscription> _subscriptionsRead;
    private readonly IRepository<TenantSubscription> _subscriptionsWrite;
    private readonly IReadRepository<BillingCheckoutSession> _sessionsRead;
    private readonly IRepository<BillingCheckoutSession> _sessionsWrite;

    public FinalizeSubscriptionCheckoutCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<TenantSubscription> subscriptionsRead,
        IRepository<TenantSubscription> subscriptionsWrite,
        IReadRepository<BillingCheckoutSession> sessionsRead,
        IRepository<BillingCheckoutSession> sessionsWrite)
    {
        _tenantContext = tenantContext;
        _subscriptionsRead = subscriptionsRead;
        _subscriptionsWrite = subscriptionsWrite;
        _sessionsRead = sessionsRead;
        _sessionsWrite = sessionsWrite;
    }

    public async Task<Result<SubscriptionCheckoutSessionDto>> Handle(FinalizeSubscriptionCheckoutCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Tenants.AccessDenied",
                "Bu kiracının checkout oturumunu finalize etme yetkisi yok.");
        }

        var session = await _sessionsRead.FirstOrDefaultAsync(
            new BillingCheckoutSessionByTenantAndIdSpec(request.TenantId, request.CheckoutSessionId), ct);
        if (session is null)
            return Result<SubscriptionCheckoutSessionDto>.Failure("Subscriptions.CheckoutSessionNotFound", "Checkout session bulunamadı.");

        var utcNow = DateTime.UtcNow;
        if (!session.IsOpen(utcNow))
        {
            if (session.Status is BillingCheckoutSessionStatus.Pending or BillingCheckoutSessionStatus.RedirectReady)
            {
                session.MarkExpired(utcNow);
                await _sessionsWrite.UpdateAsync(session, ct);
                await _sessionsWrite.SaveChangesAsync(ct);
            }

            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.CheckoutSessionNotOpen",
                "Checkout session kapanmış; finalize edilemez.");
        }

        var sub = await _subscriptionsRead.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(request.TenantId), ct);
        if (sub is null)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı.");
        }

        sub.ActivatePaidPlan(session.TargetPlanCode, utcNow);
        session.MarkCompleted(utcNow, request.ExternalReference);

        await _subscriptionsWrite.UpdateAsync(sub, ct);
        await _sessionsWrite.UpdateAsync(session, ct);
        await _sessionsWrite.SaveChangesAsync(ct);

        var dto = new SubscriptionCheckoutSessionDto(
            session.Id,
            session.TenantId,
            SubscriptionPlanCatalog.ToApiCode(session.CurrentPlanCode),
            SubscriptionPlanCatalog.ToApiCode(session.TargetPlanCode),
            session.Status,
            session.Provider,
            session.CheckoutUrl,
            false,
            session.ExpiresAtUtc);

        return Result<SubscriptionCheckoutSessionDto>.Success(dto);
    }
}

