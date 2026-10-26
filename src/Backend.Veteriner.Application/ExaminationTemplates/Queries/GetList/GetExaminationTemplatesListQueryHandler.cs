using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ExaminationTemplates.Queries.GetList;

/// <summary>Arama sözlükle aynı kuraldır (<see cref="ClinicalTermSearch.BuildTokenPatterns"/>): Türkçe duyarsız, her kelime adda geçmeli.</summary>
public sealed class GetExaminationTemplatesListQueryHandler
    : IRequestHandler<GetExaminationTemplatesListQuery, Result<PagedResult<ExaminationTemplateDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ExaminationTemplate> _templates;
    private readonly IReadRepository<Product> _products;

    public GetExaminationTemplatesListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<ExaminationTemplate> templates,
        IReadRepository<Product> products)
    {
        _tenantContext = tenantContext;
        _templates = templates;
        _products = products;
    }

    public async ValueTask<Result<PagedResult<ExaminationTemplateDto>>> Handle(
        GetExaminationTemplatesListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<PagedResult<ExaminationTemplateDto>>.Failure(ExaminationTemplateErrors.TenantContextMissing);

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var tokens = ClinicalTermSearch.BuildTokenPatterns(request.PageRequest.Search);

        var total = await _templates.CountAsync(
            new ExaminationTemplatesFilteredCountSpec(tenantId, request.IncludeInactive, tokens), ct);

        var rows = await _templates.ListAsync(
            new ExaminationTemplatesFilteredPagedSpec(tenantId, request.IncludeInactive, tokens, page, pageSize), ct);

        var items = await rows.ToDtosAsync(_products, tenantId, ct);
        return Result<PagedResult<ExaminationTemplateDto>>.Success(
            PagedResult<ExaminationTemplateDto>.Create(items.ToList(), total, page, pageSize));
    }
}
