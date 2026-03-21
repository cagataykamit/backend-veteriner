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
    private readonly IReadRepository<Clinic> _clinics;

    public GetClinicByIdQueryHandler(ITenantContext tenantContext, IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
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

        var dto = new ClinicDetailDto(clinic.Id, clinic.TenantId, clinic.Name, clinic.City, clinic.IsActive);
        return Result<ClinicDetailDto>.Success(dto);
    }
}
