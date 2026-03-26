using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetById;

public sealed class GetVaccinationByIdQueryHandler
    : IRequestHandler<GetVaccinationByIdQuery, Result<VaccinationDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Vaccination> _vaccinations;

    public GetVaccinationByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Vaccination> vaccinations)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _vaccinations = vaccinations;
    }

    public async Task<Result<VaccinationDetailDto>> Handle(GetVaccinationByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VaccinationDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var v = await _vaccinations.FirstOrDefaultAsync(
            new VaccinationByIdSpec(tenantId, request.Id), ct);
        if (v is null)
            return Result<VaccinationDetailDto>.Failure("Vaccinations.NotFound", "Aşı kaydı bulunamadı.");
        if (_clinicContext.ClinicId is { } clinicId && v.ClinicId != clinicId)
            return Result<VaccinationDetailDto>.Failure("Vaccinations.NotFound", "Asi kaydi bulunamadi.");

        var dto = new VaccinationDetailDto(
            v.Id,
            v.TenantId,
            v.PetId,
            v.ClinicId,
            v.ExaminationId,
            v.VaccineName,
            v.AppliedAtUtc,
            v.DueAtUtc,
            v.Status,
            v.Notes);
        return Result<VaccinationDetailDto>.Success(dto);
    }
}
