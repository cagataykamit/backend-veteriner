using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetExampleClinicStatus;

/// <summary>
/// Temizlikle aynı yetki ve kiracı eşleşmesi (<c>Subscriptions.Manage</c>, JWT kiracısı == route); sayılar temizliğin
/// kullandığı kuraldan (<see cref="IExampleClinicCleaner"/>) gelir.
/// </summary>
public sealed class GetExampleClinicStatusQueryHandler
    : IRequestHandler<GetExampleClinicStatusQuery, Result<ExampleClinicStatusDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IExampleClinicCleaner _cleaner;

    public GetExampleClinicStatusQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IExampleClinicCleaner cleaner)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _cleaner = cleaner;
    }

    public async Task<Result<ExampleClinicStatusDto>> Handle(GetExampleClinicStatusQuery request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Subscriptions.Manage))
        {
            return Result<ExampleClinicStatusDto>.Failure(
                "Auth.PermissionDenied",
                "Örnek klinik durumu için Subscriptions.Manage yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<ExampleClinicStatusDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<ExampleClinicStatusDto>.Failure(
                "Tenants.AccessDenied",
                "Örnek klinik durumu yalnızca oturumdaki kiracı için okunabilir.");
        }

        return Result<ExampleClinicStatusDto>.Success(await _cleaner.GetStatusAsync(request.TenantId, ct));
    }
}
