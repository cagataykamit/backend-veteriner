using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionSummary;

public sealed class GetTenantSubscriptionSummaryQueryHandler
    : IRequestHandler<GetTenantSubscriptionSummaryQuery, Result<TenantSubscriptionSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _effectiveWriteEvaluator;

    public GetTenantSubscriptionSummaryQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions,
        TenantSubscriptionEffectiveWriteEvaluator effectiveWriteEvaluator)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _tenants = tenants;
        _subscriptions = subscriptions;
        _effectiveWriteEvaluator = effectiveWriteEvaluator;
    }

    public async Task<Result<TenantSubscriptionSummaryDto>> Handle(
        GetTenantSubscriptionSummaryQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<TenantSubscriptionSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var hasSubsRead = _permissions.HasPermission(PermissionCatalog.Subscriptions.Read);
        var hasTenantsRead = _permissions.HasPermission(PermissionCatalog.Tenants.Read);
        if (!hasSubsRead && !hasTenantsRead)
        {
            return Result<TenantSubscriptionSummaryDto>.Failure(
                "Auth.PermissionDenied",
                "Abonelik özeti için Subscriptions.Read veya Tenants.Read yetkisi gerekir.");
        }

        if (request.TenantId != jwtTenantId && !hasTenantsRead)
        {
            return Result<TenantSubscriptionSummaryDto>.Failure(
                "Tenants.AccessDenied",
                "Bu kiracının abonelik özetine erişim yok.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
            return Result<TenantSubscriptionSummaryDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        var sub = await _subscriptions.FirstOrDefaultAsync(
            new TenantSubscriptionByTenantIdSpec(request.TenantId), ct);
        if (sub is null)
        {
            return Result<TenantSubscriptionSummaryDto>.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı.");
        }

        var utcNow = DateTime.UtcNow;
        var effectiveStatus = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, utcNow);
        int? daysRemaining = null;
        if (sub.Status == TenantSubscriptionStatus.Trialing && sub.TrialEndsAtUtc is { } trialEnd)
        {
            var span = trialEnd - utcNow;
            daysRemaining = span.TotalDays <= 0 ? 0 : (int)Math.Ceiling(span.TotalDays);
        }

        var isReadOnly = !TenantSubscriptionEffectiveWriteEvaluator.WriteAllowed(effectiveStatus);
        var canManage = _permissions.HasPermission(PermissionCatalog.Tenants.Create);

        var planCodeStr = SubscriptionPlanCatalog.ToApiCode(sub.PlanCode);
        var planName = SubscriptionPlanCatalog.GetName(sub.PlanCode);
        var available = SubscriptionPlanCatalog.All
            .Select(p => new SubscriptionPlanOptionDto(
                SubscriptionPlanCatalog.ToApiCode(p.Code),
                p.Name,
                p.Description,
                p.MaxUsers))
            .ToList();

        var dto = new TenantSubscriptionSummaryDto(
            tenant.Id,
            tenant.Name,
            planCodeStr,
            planName,
            effectiveStatus,
            sub.TrialStartsAtUtc,
            sub.TrialEndsAtUtc,
            daysRemaining,
            isReadOnly,
            canManage,
            available);

        return Result<TenantSubscriptionSummaryDto>.Success(dto);
    }
}
