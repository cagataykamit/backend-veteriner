using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Deactivate;

/// <summary>
/// Tenant-scoped klinik pasife alma (Faz 5A). Idempotent: zaten pasifse <c>AlreadyInactive = true</c> döner, yazma olmaz.
/// "Tenantta en az 1 aktif klinik kalsın" kuralı bu fazda uygulanmaz (out-of-scope).
/// Read-only / cancelled tenant durumunda merkezi <c>TenantSubscriptionWriteGuardBehavior</c> command'ı önce keser.
/// </summary>
public sealed class DeactivateClinicCommandHandler
    : IRequestHandler<DeactivateClinicCommand, Result<DeactivateClinicResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IRepository<Clinic> _clinicsWrite;

    public DeactivateClinicCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinicsRead,
        IRepository<Clinic> clinicsWrite)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinicsRead = clinicsRead;
        _clinicsWrite = clinicsWrite;
    }

    public async Task<Result<DeactivateClinicResultDto>> Handle(DeactivateClinicCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Clinics.Update))
        {
            return Result<DeactivateClinicResultDto>.Failure(
                "Auth.PermissionDenied",
                "Kliniği pasife almak için Clinics.Update yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<DeactivateClinicResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        var clinic = await _clinicsRead.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.Id), ct);
        if (clinic is null)
        {
            return Result<DeactivateClinicResultDto>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<DeactivateClinicResultDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct))
        {
            if (!await _userClinics.ExistsAsync(userId, clinic.Id, ct))
            {
                return Result<DeactivateClinicResultDto>.Failure(
                    "Clinics.AccessDenied",
                    "Bu klinik için atanmış üyeliğiniz yok.");
            }
        }

        if (!clinic.IsActive)
        {
            return Result<DeactivateClinicResultDto>.Success(
                new DeactivateClinicResultDto(clinic.Id, AlreadyInactive: true));
        }

        clinic.Deactivate();
        await _clinicsWrite.SaveChangesAsync(ct);

        return Result<DeactivateClinicResultDto>.Success(
            new DeactivateClinicResultDto(clinic.Id, AlreadyInactive: false));
    }
}
