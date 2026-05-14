using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.VaccineDefinitions.Contracts.Dtos;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.VaccineDefinitions.Queries.GetList;

public sealed class GetVaccineDefinitionsListQueryHandler
    : IRequestHandler<GetVaccineDefinitionsListQuery, Result<PagedResult<VaccineDefinitionDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<VaccineDefinition> _definitions;

    public GetVaccineDefinitionsListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<VaccineDefinition> definitions)
    {
        _tenantContext = tenantContext;
        _definitions = definitions;
    }

    public async Task<Result<PagedResult<VaccineDefinitionDto>>> Handle(
        GetVaccineDefinitionsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<VaccineDefinitionDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        var searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);

        var total = await _definitions.CountAsync(
            new VaccineDefinitionsVisibleFilteredCountSpec(
                tenantId,
                request.IncludeInactive,
                request.SpeciesId,
                searchPattern),
            ct);

        var rows = await _definitions.ListAsync(
            new VaccineDefinitionsVisibleFilteredPagedSpec(
                tenantId,
                page,
                pageSize,
                request.IncludeInactive,
                request.SpeciesId,
                searchPattern),
            ct);

        var items = rows
            .Select(v => new VaccineDefinitionDto(
                v.Id,
                v.TenantId,
                v.SpeciesId,
                v.Name,
                v.Code,
                v.Description,
                v.DefaultNextDueDays,
                v.IsCore,
                v.IsActive))
            .ToList();

        return Result<PagedResult<VaccineDefinitionDto>>.Success(
            PagedResult<VaccineDefinitionDto>.Create(items, total, page, pageSize));
    }
}
