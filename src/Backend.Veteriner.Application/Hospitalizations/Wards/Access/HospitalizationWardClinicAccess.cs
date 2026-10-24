using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Access;

/// <inheritdoc cref="IHospitalizationWardClinicAccess"/>
public sealed class HospitalizationWardClinicAccess : IHospitalizationWardClinicAccess
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;

    public HospitalizationWardClinicAccess(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
    }

    public Task<Result<Guid>> EnsureReadAccessAsync(Guid clinicId, CancellationToken ct)
        => EnsureClinicAccessAsync(clinicId, ct);

    public Task<Result<Guid>> EnsureWriteAccessAsync(Guid clinicId, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Clinics.Update))
        {
            return Task.FromResult(Result<Guid>.Failure(
                "Auth.PermissionDenied",
                "Yatış bölüm/oda tanımlarını değiştirmek için Clinics.Update yetkisi gerekir."));
        }

        return EnsureClinicAccessAsync(clinicId, ct);
    }

    private async Task<Result<Guid>> EnsureClinicAccessAsync(Guid clinicId, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<Guid>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı.");

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct)
            && !await _userClinics.ExistsAsync(userId, clinic.Id, ct))
        {
            return Result<Guid>.Failure(
                "Clinics.AccessDenied",
                "Bu klinik için atanmış üyeliğiniz yok.");
        }

        return Result<Guid>.Success(tenantId);
    }
}
