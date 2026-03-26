using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetList;

public sealed class GetExaminationsListQueryHandler
    : IRequestHandler<GetExaminationsListQuery, Result<PagedResult<ExaminationListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Examination> _examinations;

    public GetExaminationsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Examination> examinations)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _examinations = examinations;
    }

    public async Task<Result<PagedResult<ExaminationListItemDto>>> Handle(
        GetExaminationsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ExaminationListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var effectiveClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<ExaminationListItemDto>>.Failure(
                "Examinations.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var total = await _examinations.CountAsync(
            new ExaminationsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.AppointmentId,
                request.DateFromUtc,
                request.DateToUtc),
            ct);

        var rows = await _examinations.ListAsync(
            new ExaminationsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.AppointmentId,
                request.DateFromUtc,
                request.DateToUtc,
                page,
                pageSize),
            ct);

        var items = rows
            .Select(e => new ExaminationListItemDto(
                e.Id,
                e.ClinicId,
                e.PetId,
                e.AppointmentId,
                e.ExaminedAtUtc,
                e.VisitReason))
            .ToList();

        return Result<PagedResult<ExaminationListItemDto>>.Success(
            PagedResult<ExaminationListItemDto>.Create(items, total, page, pageSize));
    }
}
