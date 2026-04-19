using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Activate;

/// <summary>
/// Tenant-scoped klinik tekrar aktifleştirme (Faz 5A). Idempotent: zaten aktifse <c>AlreadyActive = true</c> döner, yazma olmaz.
/// Read-only / cancelled tenant durumunda merkezi <c>TenantSubscriptionWriteGuardBehavior</c> command'ı önce keser.
/// </summary>
public sealed class ActivateClinicCommandHandler
    : IRequestHandler<ActivateClinicCommand, Result<ActivateClinicResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IRepository<Clinic> _clinicsWrite;

    public ActivateClinicCommandHandler(
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

    public async Task<Result<ActivateClinicResultDto>> Handle(ActivateClinicCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Clinics.Update))
        {
            return Result<ActivateClinicResultDto>.Failure(
                "Auth.PermissionDenied",
                "Kliniği aktifleştirmek için Clinics.Update yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ActivateClinicResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        var clinic = await _clinicsRead.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.Id), ct);
        if (clinic is null)
        {
            return Result<ActivateClinicResultDto>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil.");
        }

        if (clinic.IsActive)
        {
            return Result<ActivateClinicResultDto>.Success(
                new ActivateClinicResultDto(clinic.Id, AlreadyActive: true));
        }

        clinic.Activate();
        await _clinicsWrite.SaveChangesAsync(ct);

        return Result<ActivateClinicResultDto>.Success(
            new ActivateClinicResultDto(clinic.Id, AlreadyActive: false));
    }
}
