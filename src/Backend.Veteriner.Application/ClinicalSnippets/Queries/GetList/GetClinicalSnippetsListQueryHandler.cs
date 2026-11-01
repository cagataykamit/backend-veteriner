using Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalSnippets.Specs;
using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalSnippets.Queries.GetList;

public sealed class GetClinicalSnippetsListQueryHandler
    : IRequestHandler<GetClinicalSnippetsListQuery, Result<PagedResult<ClinicalSnippetDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ClinicalSnippet> _snippets;

    public GetClinicalSnippetsListQueryHandler(ITenantContext tenantContext, IReadRepository<ClinicalSnippet> snippets)
    {
        _tenantContext = tenantContext;
        _snippets = snippets;
    }

    public async ValueTask<Result<PagedResult<ClinicalSnippetDto>>> Handle(GetClinicalSnippetsListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<PagedResult<ClinicalSnippetDto>>.Failure(ClinicalSnippetErrors.TenantContextMissing);

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, ClinicalSnippet.MaxPerTenant);
        var tokens = ClinicalTermSearch.BuildTokenPatterns(request.PageRequest.Search);

        var total = await _snippets.CountAsync(new ClinicalSnippetsFilteredCountSpec(tenantId, request.IncludeInactive, tokens), ct);
        var rows = await _snippets.ListAsync(
            new ClinicalSnippetsFilteredPagedSpec(tenantId, request.IncludeInactive, tokens, page, pageSize), ct);

        return Result<PagedResult<ClinicalSnippetDto>>.Success(
            PagedResult<ClinicalSnippetDto>.Create(rows.Select(ClinicalSnippetDto.From).ToList(), total, page, pageSize));
    }
}
