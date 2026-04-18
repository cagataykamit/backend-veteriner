using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.AssignMemberClinic;

/// <summary>
/// Tenant-scoped klinik üyelik ataması (Faz 4B). Global admin yüzeyine düşmez. Güvenlik katmanları:
/// <list type="number">
///   <item><c>Tenants.InviteCreate</c> yetkisi zorunlu (Faz 3B ile aynı çizgi; yeni permission açılmadı).</item>
///   <item>JWT <c>tenant_id</c> == route <c>tenantId</c> — aksi halde <c>Tenants.AccessDenied</c>.</item>
///   <item>Üye bu kiracının <c>UserTenant</c> satırında yoksa 404 <c>Members.NotFound</c> (sızma maskelemesi).</item>
///   <item>Klinik bu kiracıda yoksa <c>Clinics.NotFound</c>; pasifse <c>Clinics.Inactive</c>.</item>
/// </list>
/// Idempotent: ilişki zaten varsa kayıt eklenmez, <c>AlreadyAssigned = true</c> döner.
/// Read-only / cancelled tenant durumunda merkezi <c>TenantSubscriptionWriteGuardBehavior</c> bu command'ı önce keser.
/// Permission cache invalidation yapılmaz: clinic membership permission setini değiştirmez.
/// </summary>
public sealed class AssignTenantMemberClinicCommandHandler
    : IRequestHandler<AssignTenantMemberClinicCommand, Result<AssignTenantMemberClinicResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IUserTenantRepository _userTenantRepo;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IUserClinicRepository _userClinics;
    private readonly IUnitOfWork _uow;

    public AssignTenantMemberClinicCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IUserTenantRepository userTenantRepo,
        IReadRepository<Clinic> clinicsRead,
        IUserClinicRepository userClinics,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _userTenantRepo = userTenantRepo;
        _clinicsRead = clinicsRead;
        _userClinics = userClinics;
        _uow = uow;
    }

    public async Task<Result<AssignTenantMemberClinicResultDto>> Handle(
        AssignTenantMemberClinicCommand request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<AssignTenantMemberClinicResultDto>.Failure(
                "Auth.PermissionDenied",
                "Kiracı üyesine klinik atamak için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<AssignTenantMemberClinicResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<AssignTenantMemberClinicResultDto>.Failure(
                "Tenants.AccessDenied",
                "Klinik ataması yalnızca oturumdaki kiracı bağlamında yapılabilir.");
        }

        if (!await _userTenantRepo.ExistsAsync(request.MemberId, request.TenantId, ct))
        {
            return Result<AssignTenantMemberClinicResultDto>.Failure(
                "Members.NotFound",
                "Üye bu kiracıda bulunamadı.");
        }

        var clinic = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByIdSpec(request.TenantId, request.ClinicId), ct);
        if (clinic is null)
        {
            return Result<AssignTenantMemberClinicResultDto>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil.");
        }

        if (!clinic.IsActive)
        {
            return Result<AssignTenantMemberClinicResultDto>.Failure(
                "Clinics.Inactive",
                "Seçilen klinik pasif; atama yapılamaz.");
        }

        if (await _userClinics.ExistsAsync(request.MemberId, request.ClinicId, ct))
        {
            return Result<AssignTenantMemberClinicResultDto>.Success(new AssignTenantMemberClinicResultDto(
                request.MemberId,
                clinic.Id,
                clinic.Name,
                AlreadyAssigned: true));
        }

        var entity = new UserClinic(request.MemberId, request.ClinicId);
        await _userClinics.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<AssignTenantMemberClinicResultDto>.Success(new AssignTenantMemberClinicResultDto(
            request.MemberId,
            clinic.Id,
            clinic.Name,
            AlreadyAssigned: false));
    }
}
