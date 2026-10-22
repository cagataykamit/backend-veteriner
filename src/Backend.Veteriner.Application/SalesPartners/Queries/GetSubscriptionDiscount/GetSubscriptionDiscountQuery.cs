using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.SalesPartners.Queries.GetSubscriptionDiscount;

/// <summary>Kiracının ortak kodu durumu (abonelik sayfası).</summary>
public sealed record GetSubscriptionDiscountQuery(Guid TenantId) : IRequest<Result<SubscriptionDiscountDto>>;

public sealed class GetSubscriptionDiscountQueryHandler : IRequestHandler<GetSubscriptionDiscountQuery, Result<SubscriptionDiscountDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<DiscountRedemption> _redemptions;
    private readonly IReadRepository<TenantSubscription> _subscriptions;

    public GetSubscriptionDiscountQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<DiscountRedemption> redemptions,
        IReadRepository<TenantSubscription> subscriptions)
    {
        _tenantContext = tenantContext;
        _redemptions = redemptions;
        _subscriptions = subscriptions;
    }

    public async Task<Result<SubscriptionDiscountDto>> Handle(GetSubscriptionDiscountQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId || tenantId != request.TenantId)
            return Result<SubscriptionDiscountDto>.Failure(SalesPartnerErrors.AccessDenied);

        var redemption = await _redemptions.FirstOrDefaultAsync(new DiscountRedemptionByTenantSpec(tenantId), ct);
        if (redemption is not null)
            return Result<SubscriptionDiscountDto>.Success(SubscriptionDiscountDto.From(redemption, canApply: false));

        var subscription = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        var canApply = subscription is not null && !subscription.ActivatedAtUtc.HasValue;
        return Result<SubscriptionDiscountDto>.Success(SubscriptionDiscountDto.From(null, canApply));
    }
}
