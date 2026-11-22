using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.SearchProducts;

public sealed class SearchLicensedProductsQueryHandler
    : IRequestHandler<SearchLicensedProductsQuery, Result<PagedResult<LicensedProductDto>>>
{
    private readonly IReadRepository<LicensedProduct> _products;

    public SearchLicensedProductsQueryHandler(IReadRepository<LicensedProduct> products) => _products = products;

    public async ValueTask<Result<PagedResult<LicensedProductDto>>> Handle(SearchLicensedProductsQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 100);
        var tokens = ListQueryTextSearch.BuildKeyTokenPatterns(request.PageRequest.Search, DrugGuideErrors.MaxSearchTokens);

        var total = await _products.CountAsync(new LicensedProductsCountSpec(tokens, request.Field, request.ActiveIngredientId), ct);
        var rows = total == 0
            ? []
            : await _products.ListAsync(new LicensedProductsPagedSpec(tokens, request.Field, request.ActiveIngredientId, page, pageSize), ct);

        return Result<PagedResult<LicensedProductDto>>.Success(
            PagedResult<LicensedProductDto>.Create(rows.Select(LicensedProductDto.From).ToList(), total, page, pageSize));
    }
}
