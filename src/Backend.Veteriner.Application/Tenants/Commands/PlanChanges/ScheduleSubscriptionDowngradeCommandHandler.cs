using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.PlanChanges;

public sealed class ScheduleSubscriptionDowngradeCommandHandler
    : IRequestHandler<ScheduleSubscriptionDowngradeCommand, Result<PendingSubscriptionPlanChangeDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<TenantSubscription> _subscriptionsRead;
    private readonly IRepository<ScheduledSubscriptionPlanChange> _changesWrite;
    private readonly IReadRepository<ScheduledSubscriptionPlanChange> _changesRead;

    public ScheduleSubscriptionDowngradeCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<TenantSubscription> subscriptionsRead,
        IRepository<ScheduledSubscriptionPlanChange> changesWrite,
        IReadRepository<ScheduledSubscriptionPlanChange> changesRead)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _subscriptionsRead = subscriptionsRead;
        _changesWrite = changesWrite;
        _changesRead = changesRead;
    }

    public async Task<Result<PendingSubscriptionPlanChangeDto>> Handle(ScheduleSubscriptionDowngradeCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Tenants.AccessDenied", "Bu kiracı için plan değişikliği yetkisi yok.");

        if (!SubscriptionPlanCatalog.TryParseApiCode(request.TargetPlanCode, out var target))
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Subscriptions.PlanCodeInvalid", "Hedef plan kodu geçersiz.");

        var sub = await _subscriptionsRead.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(request.TenantId), ct);
        if (sub is null)
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Subscriptions.NotFound", "Bu kiracı için abonelik kaydı bulunamadı.");

        var decision = SubscriptionPlanChangeDecider.Decide(sub.PlanCode, target);
        if (decision == SubscriptionPlanChangeDecision.Same)
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Subscriptions.SamePlanAlreadyActive", "Kiracı zaten seçilen planda.");
        if (decision != SubscriptionPlanChangeDecision.Downgrade)
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Subscriptions.UpgradeRequiresCheckout", "Upgrade için checkout akışı kullanılmalıdır.");

        var effectiveStatus = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, DateTime.UtcNow);
        if (effectiveStatus == TenantSubscriptionStatus.Cancelled)
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Subscriptions.TenantCancelled", "İptal edilmiş abonelikte plan değişikliği yapılamaz.");

        var now = DateTime.UtcNow;
        var open = await _changesRead.FirstOrDefaultAsync(new OpenScheduledPlanChangeByTenantSpec(request.TenantId), ct);
        if (open is not null)
        {
            // Tek açık kayıt politikası: yenisi eskisini replace eder.
            open.Cancel(now);
            await _changesWrite.UpdateAsync(open, ct);
        }

        var requestedBy = _clientContext.UserId ?? Guid.Empty;
        if (requestedBy == Guid.Empty)
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var effectiveAt = ResolveEffectiveAt(sub, now);
        var change = ScheduledSubscriptionPlanChange.CreateScheduledDowngrade(
            request.TenantId,
            sub.PlanCode,
            target,
            requestedBy,
            now,
            effectiveAt,
            request.Reason);

        await _changesWrite.AddAsync(change, ct);
        await _changesWrite.SaveChangesAsync(ct);

        return Result<PendingSubscriptionPlanChangeDto>.Success(Map(change));
    }

    private static DateTime ResolveEffectiveAt(TenantSubscription sub, DateTime utcNow)
    {
        var period = TenantSubscriptionPeriodCalculator.ResolveCurrentWindow(sub, utcNow);
        if (period.PeriodEndUtc > utcNow)
            return period.PeriodEndUtc;
        return utcNow.AddDays(1);
    }

    private static PendingSubscriptionPlanChangeDto Map(ScheduledSubscriptionPlanChange change)
        => new(
            change.Id,
            SubscriptionPlanCatalog.ToApiCode(change.CurrentPlanCode),
            SubscriptionPlanCatalog.ToApiCode(change.TargetPlanCode),
            change.ChangeType,
            change.Status,
            change.RequestedAtUtc,
            change.EffectiveAtUtc,
            change.Reason);
}
