using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionCheckout;

public sealed class GetSubscriptionCheckoutQueryHandler
    : IRequestHandler<GetSubscriptionCheckoutQuery, Result<SubscriptionCheckoutSessionDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<BillingCheckoutSession> _sessionsWrite;
    private readonly IReadRepository<BillingCheckoutSession> _sessionsRead;

    public GetSubscriptionCheckoutQueryHandler(
        ITenantContext tenantContext,
        IRepository<BillingCheckoutSession> sessionsWrite,
        IReadRepository<BillingCheckoutSession> sessionsRead)
    {
        _tenantContext = tenantContext;
        _sessionsWrite = sessionsWrite;
        _sessionsRead = sessionsRead;
    }

    public async Task<Result<SubscriptionCheckoutSessionDto>> Handle(GetSubscriptionCheckoutQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Tenants.AccessDenied",
                "Bu kiracının checkout oturumunu görüntüleme yetkisi yok.");
        }

        var session = await _sessionsRead.FirstOrDefaultAsync(
            new BillingCheckoutSessionByTenantAndIdSpec(request.TenantId, request.CheckoutSessionId), ct);
        if (session is null)
            return Result<SubscriptionCheckoutSessionDto>.Failure("Subscriptions.CheckoutSessionNotFound", "Checkout session bulunamadı.");

        var utcNow = DateTime.UtcNow;
        if (session.Status is BillingCheckoutSessionStatus.Pending or BillingCheckoutSessionStatus.RedirectReady
            && session.ExpiresAtUtc.HasValue
            && session.ExpiresAtUtc.Value <= utcNow)
        {
            session.MarkExpired(utcNow);
            await _sessionsWrite.UpdateAsync(session, ct);
            await _sessionsWrite.SaveChangesAsync(ct);
        }

        var dto = new SubscriptionCheckoutSessionDto(
            session.Id,
            session.TenantId,
            SubscriptionPlanCatalog.ToApiCode(session.CurrentPlanCode),
            SubscriptionPlanCatalog.ToApiCode(session.TargetPlanCode),
            session.Status,
            session.Provider,
            session.CheckoutUrl,
            session.IsOpen(utcNow),
            session.ExpiresAtUtc,
            null,
            null,
            null);

        return Result<SubscriptionCheckoutSessionDto>.Success(dto);
    }
}

