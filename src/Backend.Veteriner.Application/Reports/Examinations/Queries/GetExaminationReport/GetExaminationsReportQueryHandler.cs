using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Examinations;
using Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Examinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Examinations.Queries.GetExaminationReport;

public sealed class GetExaminationsReportQueryHandler
    : IRequestHandler<GetExaminationsReportQuery, Result<ExaminationReportResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public GetExaminationsReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Examination> examinations,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _examinations = examinations;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
    }

    public async Task<Result<ExaminationReportResultDto>> Handle(
        GetExaminationsReportQuery request,
        CancellationToken ct)
    {
        var validated = await ExaminationsReportQueryValidation.ValidateAsync(
            _tenantContext,
            _clinicContext,
            _clinics,
            request.ClinicId,
            request.FromUtc,
            request.ToUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<ExaminationReportResultDto>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, fromUtc, toUtc) = validated.Value!;

        var restricted = await ExaminationsReportClientPetFilter.ResolveAsync(
            tenantId,
            request.ClientId,
            request.PetId,
            _pets,
            ct);
        if (!restricted.IsSuccess)
            return Result<ExaminationReportResultDto>.Failure(restricted.Error);
        if (restricted.Value!.SkipQueryEmpty)
            return Result<ExaminationReportResultDto>.Success(new ExaminationReportResultDto(0, []));

        var restrictedPetIds = restricted.Value.RestrictedPetIds;

        var searchPattern = await ExaminationsReportSearchHelper.ResolveAsync(
            tenantId,
            request.Search,
            _clients,
            _pets,
            ct);

        var total = await _examinations.CountAsync(
            new ExaminationsReportFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                request.AppointmentId,
                fromUtc,
                toUtc,
                searchPattern.Pattern,
                searchPattern.PetIds),
            ct);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, ExaminationsReportConstants.MaxPageSize);

        var rows = await _examinations.ListAsync(
            new ExaminationsReportFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                restrictedPetIds,
                request.AppointmentId,
                fromUtc,
                toUtc,
                page,
                pageSize,
                searchPattern.Pattern,
                searchPattern.PetIds),
            ct);

        var items = await ExaminationsReportItemMapping.MapAsync(tenantId, rows, _clients, _pets, _clinics, ct);

        return Result<ExaminationReportResultDto>.Success(new ExaminationReportResultDto(total, items));
    }
}
