using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionSummary;

public sealed class GetTenantSubscriptionSummaryQueryHandler
    : IRequestHandler<GetTenantSubscriptionSummaryQuery, Result<TenantSubscriptionSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<ScheduledSubscriptionPlanChange> _planChanges;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _effectiveWriteEvaluator;
    private readonly ILogger<GetTenantSubscriptionSummaryQueryHandler> _logger;

    public GetTenantSubscriptionSummaryQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions,
        IReadRepository<ScheduledSubscriptionPlanChange> planChanges,
        TenantSubscriptionEffectiveWriteEvaluator effectiveWriteEvaluator,
        ILogger<GetTenantSubscriptionSummaryQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _tenants = tenants;
        _subscriptions = subscriptions;
        _planChanges = planChanges;
        _effectiveWriteEvaluator = effectiveWriteEvaluator;
        _logger = logger ?? NullLogger<GetTenantSubscriptionSummaryQueryHandler>.Instance;
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

        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var querySteps = 0;
        var slowestStep = string.Empty;
        long slowestMs = 0;

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        MarkStep("tenantLookup");
        if (tenant is null)
            return Result<TenantSubscriptionSummaryDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        var sub = await _subscriptions.FirstOrDefaultAsync(
            new TenantSubscriptionByTenantIdSpec(request.TenantId), ct);
        MarkStep("subscriptionLookup");
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
        var period = TenantSubscriptionPeriodCalculator.ResolveCurrentWindow(sub, utcNow);

        var planCodeStr = SubscriptionPlanCatalog.ToApiCode(sub.PlanCode);
        var planName = SubscriptionPlanCatalog.GetName(sub.PlanCode);
        var available = SubscriptionPlanCatalog.All
            .Select(p => new SubscriptionPlanOptionDto(
                SubscriptionPlanCatalog.ToApiCode(p.Code),
                p.Name,
                p.Description,
                p.MaxUsers))
            .ToList();

        var pending = await _planChanges.FirstOrDefaultAsync(new OpenScheduledPlanChangeByTenantSpec(request.TenantId), ct);
        MarkStep("pendingPlanChangeLookup");
        PendingSubscriptionPlanChangeDto? pendingDto = null;
        if (pending is not null)
        {
            pendingDto = new PendingSubscriptionPlanChangeDto(
                pending.Id,
                SubscriptionPlanCatalog.ToApiCode(pending.CurrentPlanCode),
                SubscriptionPlanCatalog.ToApiCode(pending.TargetPlanCode),
                pending.ChangeType,
                pending.Status,
                pending.RequestedAtUtc,
                pending.EffectiveAtUtc,
                pending.Reason);
        }

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
            period.PeriodStartUtc,
            period.PeriodEndUtc,
            period.BillingCycleAnchorUtc,
            period.PeriodEndUtc,
            pendingDto,
            available);

        _logger.LogInformation(
            "Tenant subscription summary generated. TenantId={TenantId} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            request.TenantId,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<TenantSubscriptionSummaryDto>.Success(dto);
    }
}
