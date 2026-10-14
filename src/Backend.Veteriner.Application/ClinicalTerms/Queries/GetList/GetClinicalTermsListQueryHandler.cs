using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ClinicalTerms.Queries.GetList;

public sealed class GetClinicalTermsListQueryHandler
    : IRequestHandler<GetClinicalTermsListQuery, Result<PagedResult<ClinicalTermDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ClinicalTerm> _terms;

    public GetClinicalTermsListQueryHandler(ITenantContext tenantContext, IReadRepository<ClinicalTerm> terms)
    {
        _tenantContext = tenantContext;
        _terms = terms;
    }

    public async Task<Result<PagedResult<ClinicalTermDto>>> Handle(GetClinicalTermsListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<PagedResult<ClinicalTermDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var tokens = ClinicalTermSearch.BuildTokenPatterns(request.PageRequest.Search);

        var total = await _terms.CountAsync(
            new ClinicalTermsFilteredCountSpec(tenantId, request.Kind, request.IncludeInactive, tokens), ct);

        var rows = await _terms.ListAsync(
            new ClinicalTermsFilteredPagedSpec(tenantId, request.Kind, request.IncludeInactive, tokens, page, pageSize), ct);

        return Result<PagedResult<ClinicalTermDto>>.Success(
            PagedResult<ClinicalTermDto>.Create(rows.Select(ClinicalTermDto.From).ToList(), total, page, pageSize));
    }
}
