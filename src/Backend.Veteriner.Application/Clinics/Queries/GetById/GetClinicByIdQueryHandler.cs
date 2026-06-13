using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.GetById;

public sealed class GetClinicByIdQueryHandler : IRequestHandler<GetClinicByIdQuery, Result<ClinicDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;

    public GetClinicByIdQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IUserOperationClaimRepository userOperationClaims,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _userOperationClaims = userOperationClaims;
        _userClinics = userClinics;
        _clinics = clinics;
    }

    public async Task<Result<ClinicDetailDto>> Handle(GetClinicByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClinicDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClinicDetailDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        // Tenant sınırı sorguda uygulanır: başka kiracıya ait klinik hiçbir koşulda görünmez.
        // (PlatformAdmin dahil; cross-tenant erişim burada otomatik açılmaz.)
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.Id), ct);
        if (clinic is null)
            return Result<ClinicDetailDto>.Failure("Clinics.NotFound", "Klinik bulunamadı.");

        // Tenant-wide roller (Admin / Owner / PlatformAdmin) kendi kiracıları içindeki kliniği
        // UserClinic ataması olmadan okuyabilir. Diğer tüm kullanıcılar (ClinicAdmin, Veteriner,
        // Sekreter, vb.) yalnız UserClinic ile atandıkları kliniği okuyabilir.
        // ClinicAssignmentAccessGuard burada kasten kullanılmıyor: guard, claim'i olmayan veya
        // ClinicAdmin olmayan kullanıcıları kapsam dışı bıraktığı için bu IDOR'a yol açıyordu.
        var operationClaimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId, ct);
        if (!TenantWideClaimNames.IsTenantWide(operationClaimNames))
        {
            if (!await _userClinics.ExistsAsync(userId, clinic.Id, ct))
            {
                // Kaynak varlığını yetkisiz kullanıcıdan gizle: atanmamış klinik = bulunamadı.
                return Result<ClinicDetailDto>.Failure("Clinics.NotFound", "Klinik bulunamadı.");
            }
        }

        var dto = new ClinicDetailDto(
            clinic.Id,
            clinic.TenantId,
            clinic.Name,
            clinic.City,
            clinic.IsActive,
            clinic.Phone,
            clinic.Email,
            clinic.Address,
            clinic.Description);
        return Result<ClinicDetailDto>.Success(dto);
    }
}
