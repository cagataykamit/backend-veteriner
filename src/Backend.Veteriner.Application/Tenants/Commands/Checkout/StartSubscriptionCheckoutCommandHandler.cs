using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed class StartSubscriptionCheckoutCommandHandler
    : IRequestHandler<StartSubscriptionCheckoutCommand, Result<SubscriptionCheckoutSessionDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptionsRead;
    private readonly IRepository<BillingCheckoutSession> _checkoutSessionsWrite;
    private readonly IReadRepository<BillingCheckoutSession> _checkoutSessionsRead;

    public StartSubscriptionCheckoutCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptionsRead,
        IRepository<BillingCheckoutSession> checkoutSessionsWrite,
        IReadRepository<BillingCheckoutSession> checkoutSessionsRead)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _subscriptionsRead = subscriptionsRead;
        _checkoutSessionsWrite = checkoutSessionsWrite;
        _checkoutSessionsRead = checkoutSessionsRead;
    }

    public async Task<Result<SubscriptionCheckoutSessionDto>> Handle(StartSubscriptionCheckoutCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Tenants.AccessDenied",
                "Bu kiracı için checkout başlatma yetkisi yok.");
        }

        if (!SubscriptionPlanCatalog.TryParseApiCode(request.TargetPlanCode, out var targetPlanCode))
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.PlanCodeInvalid",
                "Hedef plan kodu geçersiz.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
            return Result<SubscriptionCheckoutSessionDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<SubscriptionCheckoutSessionDto>.Failure("Tenants.TenantInactive", "Pasif kiracı için checkout başlatılamaz.");

        var sub = await _subscriptionsRead.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(request.TenantId), ct);
        if (sub is null)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı.");
        }

        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, DateTime.UtcNow);
        if (effective == TenantSubscriptionStatus.Cancelled)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.TenantCancelled",
                "İptal edilmiş abonelik için checkout başlatılamaz.");
        }

        if (sub.PlanCode == targetPlanCode && effective == TenantSubscriptionStatus.Active)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.SamePlanAlreadyActive",
                "Kiracı zaten seçilen aktif planda.");
        }

        var now = DateTime.UtcNow;
        var open = await _checkoutSessionsRead.FirstOrDefaultAsync(new OpenBillingCheckoutSessionByTenantSpec(request.TenantId, now), ct);
        if (open is not null)
        {
            if (open.TargetPlanCode == targetPlanCode)
                return Result<SubscriptionCheckoutSessionDto>.Success(Map(open, now));

            open.MarkCancelled(now);
            await _checkoutSessionsWrite.UpdateAsync(open, ct);
            await _checkoutSessionsWrite.SaveChangesAsync(ct);
        }

        var expiresAt = now.AddMinutes(SubscriptionCheckoutDefaults.SessionTtlMinutes);
        var session = BillingCheckoutSession.CreatePending(
            request.TenantId,
            sub.PlanCode,
            targetPlanCode,
            BillingProvider.Manual,
            now,
            expiresAt);

        await _checkoutSessionsWrite.AddAsync(session, ct);
        await _checkoutSessionsWrite.SaveChangesAsync(ct);

        return Result<SubscriptionCheckoutSessionDto>.Success(Map(session, now));
    }

    private static SubscriptionCheckoutSessionDto Map(BillingCheckoutSession session, DateTime utcNow)
    {
        return new SubscriptionCheckoutSessionDto(
            session.Id,
            session.TenantId,
            SubscriptionPlanCatalog.ToApiCode(session.CurrentPlanCode),
            SubscriptionPlanCatalog.ToApiCode(session.TargetPlanCode),
            session.Status,
            session.Provider,
            session.CheckoutUrl,
            session.IsOpen(utcNow),
            session.ExpiresAtUtc);
    }
}

