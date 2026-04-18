using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMemberClinic;

/// <summary>
/// Tenant-scoped klinik üyelik kaldırma (Faz 4B). Global admin yüzeyine düşmez. Güvenlik katmanları:
/// <list type="number">
///   <item><c>Tenants.InviteCreate</c> yetkisi zorunlu (Faz 3B ile aynı çizgi; yeni permission açılmadı).</item>
///   <item>JWT <c>tenant_id</c> == route <c>tenantId</c> — aksi halde <c>Tenants.AccessDenied</c>.</item>
///   <item>Çağıran kendi üzerinden klinik kaldıramaz — <c>Clinics.SelfClinicRemoveForbidden</c> (kilitlenme/kendi kendini tenant'tan atma koruması).</item>
///   <item>Üye bu kiracının <c>UserTenant</c> satırında yoksa 404 <c>Members.NotFound</c>.</item>
///   <item>Klinik bu kiracıda yoksa <c>Clinics.NotFound</c>. Pasif klinik remove'a engel değildir (yanlış atamayı temizlemek mümkün olmalı).</item>
/// </list>
/// Idempotent: ilişki yoksa remove çağrılmaz, <c>AlreadyRemoved = true</c> döner.
/// Read-only / cancelled tenant durumunda merkezi <c>TenantSubscriptionWriteGuardBehavior</c> bu command'ı önce keser.
/// Permission cache invalidation yapılmaz: clinic membership permission setini değiştirmez.
/// Son-klinik koruması ve session/refresh revoke bu fazda kapsam dışıdır.
/// </summary>
public sealed class RemoveTenantMemberClinicCommandHandler
    : IRequestHandler<RemoveTenantMemberClinicCommand, Result<RemoveTenantMemberClinicResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IUserTenantRepository _userTenantRepo;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IUserClinicRepository _userClinics;
    private readonly IUnitOfWork _uow;

    public RemoveTenantMemberClinicCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IUserTenantRepository userTenantRepo,
        IReadRepository<Clinic> clinicsRead,
        IUserClinicRepository userClinics,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _userTenantRepo = userTenantRepo;
        _clinicsRead = clinicsRead;
        _userClinics = userClinics;
        _uow = uow;
    }

    public async Task<Result<RemoveTenantMemberClinicResultDto>> Handle(
        RemoveTenantMemberClinicCommand request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<RemoveTenantMemberClinicResultDto>.Failure(
                "Auth.PermissionDenied",
                "Kiracı üyesinden klinik kaldırmak için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<RemoveTenantMemberClinicResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<RemoveTenantMemberClinicResultDto>.Failure(
                "Tenants.AccessDenied",
                "Klinik kaldırma yalnızca oturumdaki kiracı bağlamında yapılabilir.");
        }

        if (_clientContext.UserId is { } callerId && callerId == request.MemberId)
        {
            return Result<RemoveTenantMemberClinicResultDto>.Failure(
                "Clinics.SelfClinicRemoveForbidden",
                "Kullanıcı kendi üzerinden klinik üyeliğini bu uçtan kaldıramaz; başka bir yönetici işlemi yapmalıdır.");
        }

        if (!await _userTenantRepo.ExistsAsync(request.MemberId, request.TenantId, ct))
        {
            return Result<RemoveTenantMemberClinicResultDto>.Failure(
                "Members.NotFound",
                "Üye bu kiracıda bulunamadı.");
        }

        var clinic = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByIdSpec(request.TenantId, request.ClinicId), ct);
        if (clinic is null)
        {
            return Result<RemoveTenantMemberClinicResultDto>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil.");
        }

        if (!await _userClinics.ExistsAsync(request.MemberId, request.ClinicId, ct))
        {
            return Result<RemoveTenantMemberClinicResultDto>.Success(new RemoveTenantMemberClinicResultDto(
                request.MemberId,
                clinic.Id,
                AlreadyRemoved: true));
        }

        await _userClinics.RemoveAsync(request.MemberId, request.ClinicId, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<RemoveTenantMemberClinicResultDto>.Success(new RemoveTenantMemberClinicResultDto(
            request.MemberId,
            clinic.Id,
            AlreadyRemoved: false));
    }
}
