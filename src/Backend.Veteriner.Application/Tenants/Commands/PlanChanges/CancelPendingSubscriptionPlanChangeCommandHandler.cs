using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.PlanChanges;

public sealed class CancelPendingSubscriptionPlanChangeCommandHandler
    : IRequestHandler<CancelPendingSubscriptionPlanChangeCommand, Result<PendingSubscriptionPlanChangeDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ScheduledSubscriptionPlanChange> _changesWrite;
    private readonly IReadRepository<ScheduledSubscriptionPlanChange> _changesRead;

    public CancelPendingSubscriptionPlanChangeCommandHandler(
        ITenantContext tenantContext,
        IRepository<ScheduledSubscriptionPlanChange> changesWrite,
        IReadRepository<ScheduledSubscriptionPlanChange> changesRead)
    {
        _tenantContext = tenantContext;
        _changesWrite = changesWrite;
        _changesRead = changesRead;
    }

    public async Task<Result<PendingSubscriptionPlanChangeDto>> Handle(CancelPendingSubscriptionPlanChangeCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Tenants.AccessDenied", "Bu kiracı için plan değişikliği yetkisi yok.");

        var open = await _changesRead.FirstOrDefaultAsync(new OpenScheduledPlanChangeByTenantSpec(request.TenantId), ct);
        if (open is null)
            return Result<PendingSubscriptionPlanChangeDto>.Failure("Subscriptions.PendingPlanChangeNotFound", "Bekleyen plan değişikliği bulunamadı.");

        open.Cancel(DateTime.UtcNow);
        await _changesWrite.UpdateAsync(open, ct);
        await _changesWrite.SaveChangesAsync(ct);
        return Result<PendingSubscriptionPlanChangeDto>.Success(
            new PendingSubscriptionPlanChangeDto(
                open.Id,
                SubscriptionPlanCatalog.ToApiCode(open.CurrentPlanCode),
                SubscriptionPlanCatalog.ToApiCode(open.TargetPlanCode),
                open.ChangeType,
                open.Status,
                open.RequestedAtUtc,
                open.EffectiveAtUtc,
                open.Reason));
    }
}
