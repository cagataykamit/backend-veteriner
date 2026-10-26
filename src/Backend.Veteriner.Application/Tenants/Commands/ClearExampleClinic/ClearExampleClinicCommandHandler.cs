using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.ClearExampleClinic;

/// <summary>
/// Örnek klinik temizleme kuralları:
/// <list type="number">
///   <item><c>Subscriptions.Manage</c> yetkisi (yüklemeyle aynı çizgi: Sahip + Yönetici).</item>
///   <item>JWT <c>tenant_id</c> == route <c>tenantId</c> — aksi halde <c>Tenants.AccessDenied</c> (IDOR).</item>
///   <item>Kiracıda örnek veri olmalı — <c>Tenants.ExampleClinicNotLoaded</c> (409; ikinci çağrı silmez).</item>
/// </list>
/// Abonelik durumu burada denetlenmez: deneme ve ücretli abonelikte açıktır, salt okunur/iptal kiracıyı merkezi yazma
/// engeli (<c>TenantSubscriptionWriteGuardBehavior</c>) durdurur. Örnek kayıt tanımı <see cref="IExampleClinicCleaner"/>'da.
/// </summary>
public sealed class ClearExampleClinicCommandHandler
    : IRequestHandler<ClearExampleClinicCommand, Result<ExampleClinicDataCountsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IExampleClinicCleaner _cleaner;

    public ClearExampleClinicCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IExampleClinicCleaner cleaner)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _cleaner = cleaner;
    }

    public async Task<Result<ExampleClinicDataCountsDto>> Handle(ClearExampleClinicCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Subscriptions.Manage))
        {
            return Result<ExampleClinicDataCountsDto>.Failure(
                "Auth.PermissionDenied",
                "Örnek klinik verisini temizlemek için Subscriptions.Manage yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<ExampleClinicDataCountsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<ExampleClinicDataCountsDto>.Failure(
                "Tenants.AccessDenied",
                "Örnek klinik verisi yalnızca oturumdaki kiracıda temizlenebilir.");
        }

        var status = await _cleaner.GetStatusAsync(request.TenantId, ct);
        if (!status.HasExampleData)
        {
            return Result<ExampleClinicDataCountsDto>.Failure(
                "Tenants.ExampleClinicNotLoaded",
                "Kiracıda temizlenecek örnek klinik verisi yok.");
        }

        var cleared = await _cleaner.ClearAsync(request.TenantId, ct);
        return Result<ExampleClinicDataCountsDto>.Success(cleared);
    }
}
