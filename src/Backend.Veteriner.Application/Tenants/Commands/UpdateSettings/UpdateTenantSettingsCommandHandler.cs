using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.UpdateSettings;

/// <summary>
/// Tenant adminin kendi kurum adını güncellediği minimal yüzey (Faz 5B).
/// Güvenlik katmanları:
/// <list type="number">
///   <item><c>Tenants.InviteCreate</c> yetkisi zorunlu (Faz 3B/4B ile aynı çizgi; yeni permission açılmaz).</item>
///   <item>JWT <c>tenant_id</c> == route <c>tenantId</c> — aksi halde <c>Tenants.AccessDenied</c>.</item>
///   <item>Tenant yoksa <c>Tenants.NotFound</c>. Duplicate kurum adı (kendi Id hariç) <c>Tenants.DuplicateName</c>.</item>
/// </list>
/// Read-only / cancelled tenant durumunda merkezi <c>TenantSubscriptionWriteGuardBehavior</c> bu command'ı önce keser.
/// </summary>
public sealed class UpdateTenantSettingsCommandHandler
    : IRequestHandler<UpdateTenantSettingsCommand, Result<TenantDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Tenant> _tenantsRead;
    private readonly IRepository<Tenant> _tenantsWrite;

    public UpdateTenantSettingsCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Tenant> tenantsRead,
        IRepository<Tenant> tenantsWrite)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _tenantsRead = tenantsRead;
        _tenantsWrite = tenantsWrite;
    }

    public async Task<Result<TenantDetailDto>> Handle(UpdateTenantSettingsCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<TenantDetailDto>.Failure(
                "Auth.PermissionDenied",
                "Kurum ayarlarını güncellemek için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<TenantDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<TenantDetailDto>.Failure(
                "Tenants.AccessDenied",
                "Kurum ayarları yalnızca oturumdaki kiracı bağlamında güncellenebilir.");
        }

        var tenant = await _tenantsRead.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
        {
            return Result<TenantDetailDto>.Failure(
                "Tenants.NotFound",
                "Kiracı bulunamadı.");
        }

        var nameKey = request.Name.Trim().ToLowerInvariant();
        var duplicate = await _tenantsRead.FirstOrDefaultAsync(
            new TenantByNameCaseInsensitiveSpec(nameKey), ct);
        if (duplicate is not null && duplicate.Id != tenant.Id)
        {
            return Result<TenantDetailDto>.Failure(
                "Tenants.DuplicateName",
                "Aynı ada sahip bir kiracı zaten var (büyük/küçük harf ayrımı yapılmaz).");
        }

        tenant.Rename(request.Name);
        await _tenantsWrite.SaveChangesAsync(ct);

        return Result<TenantDetailDto>.Success(new TenantDetailDto(
            tenant.Id,
            tenant.Name,
            tenant.IsActive,
            tenant.CreatedAtUtc));
    }
}
