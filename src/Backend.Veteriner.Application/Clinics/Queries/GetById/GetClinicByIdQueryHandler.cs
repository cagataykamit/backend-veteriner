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
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;

    public GetClinicByIdQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
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

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.Id), ct);
        if (clinic is null)
            return Result<ClinicDetailDto>.Failure("Clinics.NotFound", "Klinik bulunamadı.");

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClinicDetailDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct))
        {
            if (!await _userClinics.ExistsAsync(userId, clinic.Id, ct))
            {
                return Result<ClinicDetailDto>.Failure(
                    "Clinics.AccessDenied",
                    "Bu klinik için atanmış üyeliğiniz yok.");
            }
        }

        var dto = new ClinicDetailDto(clinic.Id, clinic.TenantId, clinic.Name, clinic.City, clinic.IsActive);
        return Result<ClinicDetailDto>.Success(dto);
    }
}
