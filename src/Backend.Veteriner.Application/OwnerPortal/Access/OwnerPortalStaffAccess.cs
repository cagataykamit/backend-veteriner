using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.OwnerPortal.Access;

/// <summary>Klinik paneli portal işleminin bağlamı: oturumdaki kiracı, personel ve müşteri kaydı.</summary>
public sealed record OwnerPortalStaffScope(Guid TenantId, Guid UserId, Client Client);

/// <summary>
/// Klinik paneli portal uçlarının ortak kontrolleri (davet, bağ durumu, erişimi kapatma): kiracı bağlamı ve aktifliği,
/// müşterinin bu kiracıda olması, tenant-wide olmayan kullanıcının yalnız atandığı kliniğin daveti/bağı üzerinde işlem yapması.
/// </summary>
public sealed class OwnerPortalStaffAccess
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;

    public OwnerPortalStaffAccess(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _tenants = tenants;
        _clients = clients;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
    }

    public async Task<Result<OwnerPortalStaffScope>> ResolveClientAsync(Guid clientId, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<OwnerPortalStaffScope>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<OwnerPortalStaffScope>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<OwnerPortalStaffScope>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<OwnerPortalStaffScope>.Failure("Tenants.TenantInactive", "Pasif kiracı için portal işlemi yapılamaz.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, clientId), ct);
        if (client is null)
            return Result<OwnerPortalStaffScope>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        return Result<OwnerPortalStaffScope>.Success(new OwnerPortalStaffScope(tenantId, userId, client));
    }

    public async Task<Result> EnsureClinicAssignedAsync(Guid userId, Guid clinicId, CancellationToken ct)
    {
        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct)
            && !await _userClinics.ExistsAsync(userId, clinicId, ct))
        {
            return Result.Failure("Clinics.AccessDenied", "Bu klinik için atanmış üyeliğiniz yok.");
        }

        return Result.Success();
    }
}
