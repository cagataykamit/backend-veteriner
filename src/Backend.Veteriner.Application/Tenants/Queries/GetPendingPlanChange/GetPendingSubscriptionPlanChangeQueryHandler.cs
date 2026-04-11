using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetPendingPlanChange;

public sealed class GetPendingSubscriptionPlanChangeQueryHandler
    : IRequestHandler<GetPendingSubscriptionPlanChangeQuery, Result<PendingSubscriptionPlanChangeDto?>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ScheduledSubscriptionPlanChange> _changesRead;

    public GetPendingSubscriptionPlanChangeQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<ScheduledSubscriptionPlanChange> changesRead)
    {
        _tenantContext = tenantContext;
        _changesRead = changesRead;
    }

    public async Task<Result<PendingSubscriptionPlanChangeDto?>> Handle(GetPendingSubscriptionPlanChangeQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
            return Result<PendingSubscriptionPlanChangeDto?>.Failure("Tenants.AccessDenied", "Bu kiracının plan değişikliğini görüntüleme yetkisi yok.");

        var open = await _changesRead.FirstOrDefaultAsync(new OpenScheduledPlanChangeByTenantSpec(request.TenantId), ct);
        if (open is null)
            return Result<PendingSubscriptionPlanChangeDto?>.Success(null);

        var dto = new PendingSubscriptionPlanChangeDto(
            open.Id,
            SubscriptionPlanCatalog.ToApiCode(open.CurrentPlanCode),
            SubscriptionPlanCatalog.ToApiCode(open.TargetPlanCode),
            open.ChangeType,
            open.Status,
            open.RequestedAtUtc,
            open.EffectiveAtUtc,
            open.Reason);
        return Result<PendingSubscriptionPlanChangeDto?>.Success(dto);
    }
}
