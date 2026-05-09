using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ProductCategories.Contracts.Dtos;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Queries.GetList;

public sealed class GetProductCategoriesListQueryHandler
    : IRequestHandler<GetProductCategoriesListQuery, Result<PagedResult<ProductCategoryDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ProductCategory> _categories;

    public GetProductCategoriesListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<ProductCategory> categories)
    {
        _tenantContext = tenantContext;
        _categories = categories;
    }

    public async Task<Result<PagedResult<ProductCategoryDto>>> Handle(GetProductCategoriesListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<PagedResult<ProductCategoryDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        var searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);

        var total = await _categories.CountAsync(
            new ProductCategoriesFilteredCountSpec(tenantId, searchPattern, request.IsActive),
            ct);

        var rows = await _categories.ListAsync(
            new ProductCategoriesFilteredPagedSpec(tenantId, page, pageSize, searchPattern, request.IsActive),
            ct);

        var items = rows
            .Select(x => new ProductCategoryDto(x.Id, x.Name, x.Description, x.IsActive))
            .ToList();

        return Result<PagedResult<ProductCategoryDto>>.Success(
            PagedResult<ProductCategoryDto>.Create(items, total, page, pageSize));
    }
}
