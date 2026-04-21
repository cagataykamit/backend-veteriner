using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Vaccinations;
using Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Queries.GetVaccinationReport;

public sealed class GetVaccinationsReportQueryHandler
    : IRequestHandler<GetVaccinationsReportQuery, Result<VaccinationReportResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public GetVaccinationsReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _vaccinations = vaccinations;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
    }

    public async Task<Result<VaccinationReportResultDto>> Handle(
        GetVaccinationsReportQuery request,
        CancellationToken ct)
    {
        var validated = await VaccinationsReportQueryValidation.ValidateAsync(
            _tenantContext,
            _clinicContext,
            _clinics,
            request.ClinicId,
            request.FromUtc,
            request.ToUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<VaccinationReportResultDto>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, fromUtc, toUtc) = validated.Value!;

        var restricted = await VaccinationsReportClientPetFilter.ResolveAsync(
            tenantId,
            request.ClientId,
            request.PetId,
            _pets,
            ct);
        if (!restricted.IsSuccess)
            return Result<VaccinationReportResultDto>.Failure(restricted.Error);
        if (restricted.Value!.SkipQueryEmpty)
            return Result<VaccinationReportResultDto>.Success(new VaccinationReportResultDto(0, []));

        var restrictedPetIds = restricted.Value.RestrictedPetIds;

        var searchPattern = await VaccinationsReportSearchHelper.ResolveAsync(
            tenantId,
            request.Search,
            _clients,
            _pets,
            ct);

        var total = await _vaccinations.CountAsync(
            new VaccinationsReportFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                request.Status,
                fromUtc,
                toUtc,
                searchPattern.Pattern,
                searchPattern.PetIds),
            ct);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, VaccinationsReportConstants.MaxPageSize);

        var rows = await _vaccinations.ListAsync(
            new VaccinationsReportFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                request.Status,
                fromUtc,
                toUtc,
                page,
                pageSize,
                searchPattern.Pattern,
                searchPattern.PetIds),
            ct);

        var items = await VaccinationsReportItemMapping.MapAsync(tenantId, rows, _clients, _pets, _clinics, ct);

        return Result<VaccinationReportResultDto>.Success(new VaccinationReportResultDto(total, items));
    }
}
