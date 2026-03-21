using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.GetList;

public sealed class GetClinicsListQueryHandler
    : IRequestHandler<GetClinicsListQuery, Result<PagedResult<ClinicListItemDto>>>
{
    private readonly IReadRepository<Clinic> _clinics;

    public GetClinicsListQueryHandler(IReadRepository<Clinic> clinics) => _clinics = clinics;

    public async Task<Result<PagedResult<ClinicListItemDto>>> Handle(GetClinicsListQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _clinics.CountAsync(new ClinicsByTenantCountSpec(request.TenantId), ct);
        var rows = await _clinics.ListAsync(new ClinicsByTenantPagedSpec(request.TenantId, page, pageSize), ct);

        var items = rows
            .Select(c => new ClinicListItemDto(c.Id, c.TenantId, c.Name, c.City, c.IsActive))
            .ToList();

        return Result<PagedResult<ClinicListItemDto>>.Success(
            PagedResult<ClinicListItemDto>.Create(items, total, page, pageSize));
    }
}
