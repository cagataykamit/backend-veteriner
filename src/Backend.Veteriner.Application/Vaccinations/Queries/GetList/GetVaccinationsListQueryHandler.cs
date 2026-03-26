using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetList;

public sealed class GetVaccinationsListQueryHandler
    : IRequestHandler<GetVaccinationsListQuery, Result<PagedResult<VaccinationListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Vaccination> _vaccinations;

    public GetVaccinationsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Vaccination> vaccinations)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _vaccinations = vaccinations;
    }

    public async Task<Result<PagedResult<VaccinationListItemDto>>> Handle(
        GetVaccinationsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<VaccinationListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var effectiveClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<VaccinationListItemDto>>.Failure(
                "Vaccinations.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var total = await _vaccinations.CountAsync(
            new VaccinationsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.Status,
                request.DueFromUtc,
                request.DueToUtc,
                request.AppliedFromUtc,
                request.AppliedToUtc),
            ct);

        var rows = await _vaccinations.ListAsync(
            new VaccinationsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.Status,
                request.DueFromUtc,
                request.DueToUtc,
                request.AppliedFromUtc,
                request.AppliedToUtc,
                page,
                pageSize),
            ct);

        var items = rows
            .Select(v => new VaccinationListItemDto(
                v.Id,
                v.PetId,
                v.ClinicId,
                v.ExaminationId,
                v.VaccineName,
                v.AppliedAtUtc,
                v.DueAtUtc,
                v.Status))
            .ToList();

        return Result<PagedResult<VaccinationListItemDto>>.Success(
            PagedResult<VaccinationListItemDto>.Create(items, total, page, pageSize));
    }
}
