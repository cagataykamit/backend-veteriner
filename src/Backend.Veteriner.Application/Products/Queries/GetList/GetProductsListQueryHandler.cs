using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Queries.GetList;

public sealed class GetProductsListQueryHandler
    : IRequestHandler<GetProductsListQuery, Result<PagedResult<ProductDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<ProductCategory> _categories;

    public GetProductsListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Product> products,
        IReadRepository<ProductCategory> categories)
    {
        _tenantContext = tenantContext;
        _products = products;
        _categories = categories;
    }

    public async Task<Result<PagedResult<ProductDto>>> Handle(GetProductsListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<PagedResult<ProductDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        var searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);

        var total = await _products.CountAsync(
            new ProductsFilteredCountSpec(tenantId, searchPattern, request.ProductCategoryId, request.IsActive),
            ct);

        var rows = await _products.ListAsync(
            new ProductsFilteredPagedSpec(tenantId, page, pageSize, searchPattern, request.ProductCategoryId, request.IsActive),
            ct);

        var categoryIds = rows.Where(x => x.ProductCategoryId.HasValue).Select(x => x.ProductCategoryId!.Value).Distinct().ToArray();
        var categories = categoryIds.Length == 0
            ? []
            : await _categories.ListAsync(new ProductCategoriesByIdsSpec(tenantId, categoryIds), ct);
        var categoryNameById = categories.ToDictionary(x => x.Id, x => x.Name);

        var items = rows.Select(x =>
        {
            var categoryName = x.ProductCategoryId.HasValue && categoryNameById.TryGetValue(x.ProductCategoryId.Value, out var name)
                ? name
                : null;

            return new ProductDto(
                x.Id,
                x.ProductCategoryId,
                categoryName,
                x.Name,
                x.Sku,
                x.Barcode,
                x.Description,
                x.Unit,
                x.UnitPrice,
                x.Currency,
                x.IsActive);
        }).ToList();

        return Result<PagedResult<ProductDto>>.Success(PagedResult<ProductDto>.Create(items, total, page, pageSize));
    }
}
