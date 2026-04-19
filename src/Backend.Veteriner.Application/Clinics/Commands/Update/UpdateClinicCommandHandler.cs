using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Update;

/// <summary>
/// Tenant-scoped klinik güncelleme (Faz 5A). Güvenlik katmanları:
/// <list type="number">
///   <item><c>Clinics.Update</c> yetkisi zorunlu (controller + handler).</item>
///   <item>JWT <c>tenant_id</c> bağlamı zorunlu; <c>ClinicByIdSpec(tenantId, id)</c> ile çözümlenir.</item>
///   <item>Başka kiracının kliniği 404 <c>Clinics.NotFound</c> olarak maskelenir.</item>
///   <item>Aynı tenant içinde case-insensitive aynı isim 409 <c>Clinics.DuplicateName</c>.</item>
/// </list>
/// Read-only / cancelled tenant durumunda merkezi <c>TenantSubscriptionWriteGuardBehavior</c> command'ı önce keser.
/// </summary>
public sealed class UpdateClinicCommandHandler
    : IRequestHandler<UpdateClinicCommand, Result<ClinicDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IRepository<Clinic> _clinicsWrite;

    public UpdateClinicCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Clinic> clinicsRead,
        IRepository<Clinic> clinicsWrite)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _clinicsRead = clinicsRead;
        _clinicsWrite = clinicsWrite;
    }

    public async Task<Result<ClinicDetailDto>> Handle(UpdateClinicCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Clinics.Update))
        {
            return Result<ClinicDetailDto>.Failure(
                "Auth.PermissionDenied",
                "Klinik güncellemek için Clinics.Update yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClinicDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        var clinic = await _clinicsRead.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.Id), ct);
        if (clinic is null)
        {
            return Result<ClinicDetailDto>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil.");
        }

        var nameKey = request.Name.Trim().ToLowerInvariant();
        var duplicate = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByTenantAndNameCaseInsensitiveSpec(tenantId, nameKey), ct);
        if (duplicate is not null && duplicate.Id != clinic.Id)
        {
            return Result<ClinicDetailDto>.Failure(
                "Clinics.DuplicateName",
                "Bu kiracı altında aynı isimde başka bir klinik zaten var.");
        }

        clinic.UpdateDetails(request.Name, request.City);
        await _clinicsWrite.SaveChangesAsync(ct);

        return Result<ClinicDetailDto>.Success(new ClinicDetailDto(
            clinic.Id,
            clinic.TenantId,
            clinic.Name,
            clinic.City,
            clinic.IsActive));
    }
}
