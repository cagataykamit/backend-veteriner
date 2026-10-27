using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.SalesPartners.Commands.ApplySubscriptionCode;

public sealed class ApplySubscriptionDiscountCodeCommandHandler
    : IRequestHandler<ApplySubscriptionDiscountCodeCommand, Result<SubscriptionDiscountDto>>
{
    /// <summary><c>DiscountRedemptionConfiguration.TenantIndexName</c> ile aynı.</summary>
    private const string TenantIndexName = "IX_DiscountRedemptions_TenantId";

    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IUserReadRepository _users;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly DiscountCodeRedemptionService _discountCodes;
    private readonly IRepository<DiscountRedemption> _redemptions;
    private readonly TimeProvider _timeProvider;

    public ApplySubscriptionDiscountCodeCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IUserReadRepository users,
        IReadRepository<TenantSubscription> subscriptions,
        DiscountCodeRedemptionService discountCodes,
        IRepository<DiscountRedemption> redemptions,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _users = users;
        _subscriptions = subscriptions;
        _discountCodes = discountCodes;
        _redemptions = redemptions;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<SubscriptionDiscountDto>> Handle(ApplySubscriptionDiscountCodeCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId || tenantId != request.TenantId)
            return Result<SubscriptionDiscountDto>.Failure(SalesPartnerErrors.AccessDenied);

        var subscription = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        if (subscription is null)
            return Result<SubscriptionDiscountDto>.Failure("Subscriptions.NotFound", "Bu kiracı için abonelik kaydı bulunamadı.");
        if (subscription.ActivatedAtUtc.HasValue)
            return Result<SubscriptionDiscountDto>.Failure(SalesPartnerErrors.ApplyNotOpen);

        string? email = null;
        if (_clientContext.UserId is { } userId)
            email = (await _users.FirstOrDefaultAsync(new UserByIdWithRolesSpec(userId), ct))?.Email;

        var redeemed = await _discountCodes.RedeemAsync(tenantId, email, request.Code, _timeProvider.GetUtcNow().UtcDateTime, ct);
        if (!redeemed.IsSuccess)
            return Result<SubscriptionDiscountDto>.Failure(redeemed.Error);

        try
        {
            await _redemptions.AddAsync(redeemed.Value!, ct);
            await _redemptions.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SalesPartnerErrors.IsUniqueViolation(ex, TenantIndexName))
        {
            return Result<SubscriptionDiscountDto>.Failure(SalesPartnerErrors.DuplicateRedemption);
        }

        return Result<SubscriptionDiscountDto>.Success(SubscriptionDiscountDto.From(redeemed.Value, canApply: false));
    }
}
