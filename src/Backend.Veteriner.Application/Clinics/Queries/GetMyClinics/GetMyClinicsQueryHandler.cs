using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.GetMyClinics;

public sealed class GetMyClinicsQueryHandler : IRequestHandler<GetMyClinicsQuery, Result<IReadOnlyList<ClinicListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _client;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IUserTenantRepository _userTenants;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IUserOperationClaimRepository _userOperationClaims;

    public GetMyClinicsQueryHandler(
        ITenantContext tenantContext,
        IClientContext client,
        IReadRepository<Tenant> tenants,
        IUserTenantRepository userTenants,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinicsRead,
        IUserOperationClaimRepository userOperationClaims)
    {
        _tenantContext = tenantContext;
        _client = client;
        _tenants = tenants;
        _userTenants = userTenants;
        _userClinics = userClinics;
        _clinicsRead = clinicsRead;
        _userOperationClaims = userOperationClaims;
    }

    public async Task<Result<IReadOnlyList<ClinicListItemDto>>> Handle(GetMyClinicsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var userId = _client.UserId;
        if (userId is null)
        {
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure("Tenants.TenantInactive", "Pasif kiracı için klinik listelenemez.");

        if (!await _userTenants.ExistsAsync(userId.Value, tenantId, ct))
        {
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure(
                "Auth.TenantNotMember",
                "Bu kiracıda üyeliğiniz yok.");
        }

        // Tenant-wide rol whitelist: Admin / Owner / PlatformAdmin → UserClinic atamasını bypass et.
        // Diğer roller (ClinicAdmin, Veteriner, Sekreter, vb.) mevcut assignment kuralına tabi kalır.
        // ClinicAssignmentAccessGuard burada kasten kullanılmıyor: guard claim'i olmayan kullanıcıları da
        // tenant-wide saydığı için switcher davranışı için yanıltıcıdır.
        var claimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId.Value, ct);
        var isTenantWide = TenantWideClaimNames.IsTenantWide(claimNames);

        IReadOnlyList<Clinic> clinics;
        if (isTenantWide)
        {
            clinics = await _clinicsRead.ListAsync(
                new ClinicsByTenantFilteredSpec(tenantId, request.IsActive), ct);
        }
        else
        {
            clinics = await _userClinics.ListAccessibleClinicsAsync(userId.Value, tenantId, request.IsActive, ct);
        }

        var dtos = clinics
            .Select(c => new ClinicListItemDto(c.Id, c.TenantId, c.Name, c.City, c.IsActive, c.Phone, c.Email))
            .ToArray();

        return Result<IReadOnlyList<ClinicListItemDto>>.Success(dtos);
    }
}
