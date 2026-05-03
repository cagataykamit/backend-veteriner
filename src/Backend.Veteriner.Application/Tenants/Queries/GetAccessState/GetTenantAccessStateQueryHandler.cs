using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetAccessState;

/// <summary>
/// Kiracı üyesi için yazılabilirlik özeti. <c>Subscriptions.*</c> veya <c>Tenants.Read</c> permission gerektirmez.
/// </summary>
public sealed class GetTenantAccessStateQueryHandler
    : IRequestHandler<GetTenantAccessStateQuery, Result<TenantAccessStateDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IUserTenantRepository _userTenants;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;

    public GetTenantAccessStateQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IUserTenantRepository userTenants,
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _userTenants = userTenants;
        _tenants = tenants;
        _subscriptions = subscriptions;
    }

    public async Task<Result<TenantAccessStateDto>> Handle(
        GetTenantAccessStateQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<TenantAccessStateDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<TenantAccessStateDto>.Failure(
                "Tenants.AccessDenied",
                "Bu bilgi yalnızca oturumdaki kiracı bağlamında alınabilir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<TenantAccessStateDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        if (!await _userTenants.ExistsAsync(userId, request.TenantId, ct))
        {
            return Result<TenantAccessStateDto>.Failure(
                "Auth.TenantNotMember",
                "Bu kiracıda üyeliğiniz yok.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
        {
            return Result<TenantAccessStateDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        }

        if (!tenant.IsActive)
        {
            return Result<TenantAccessStateDto>.Success(new TenantAccessStateDto(
                tenant.Id,
                IsReadOnly: true,
                ReasonCode: "Tenants.TenantInactive",
                Message: "Kiracı hesabı pasif; yazma işlemleri kapalı."));
        }

        var sub = await _subscriptions.FirstOrDefaultAsync(
            new TenantSubscriptionByTenantIdSpec(request.TenantId), ct);
        if (sub is null)
        {
            return Result<TenantAccessStateDto>.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı.");
        }

        var dto = TenantAccessStateHelper.BuildForActiveTenant(tenant.Id, sub, DateTime.UtcNow);
        return Result<TenantAccessStateDto>.Success(dto);
    }
}
