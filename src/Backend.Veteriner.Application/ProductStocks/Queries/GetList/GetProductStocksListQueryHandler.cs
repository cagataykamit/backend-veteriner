using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductStocks.Queries.GetList;

public sealed class GetProductStocksListQueryHandler
    : IRequestHandler<GetProductStocksListQuery, Result<PagedResult<ProductStockDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ProductStock> _productStocks;
    private readonly IReadRepository<Clinic> _clinics;

    public GetProductStocksListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ProductStock> productStocks,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _productStocks = productStocks;
        _clinics = clinics;
    }

    public async Task<Result<PagedResult<ProductStockDto>>> Handle(
        GetProductStocksListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ProductStockDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue &&
            request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<ProductStockDto>>.Failure(
                "ProductStocks.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PagedResult<ProductStockDto>>.Failure(scopeResult.Error);

        var effectiveClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value!.AccessibleClinicIds;

        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        var searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);

        var total = await _productStocks.CountAsync(
            new ProductStocksFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                accessibleClinicIds,
                request.ProductCategoryId,
                request.ProductId,
                request.IsBelowMinimum,
                request.IsActiveProduct,
                searchPattern),
            ct);

        var rows = await _productStocks.ListAsync(
            new ProductStocksFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                accessibleClinicIds,
                request.ProductCategoryId,
                request.ProductId,
                request.IsBelowMinimum,
                request.IsActiveProduct,
                searchPattern,
                page,
                pageSize),
            ct);

        var clinicIds = rows.Select(r => r.ClinicId).Distinct().ToArray();
        var clinics = clinicIds.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantAndIdsSpec(tenantId, clinicIds), ct);
        var clinicNameById = clinics.ToDictionary(c => c.Id, c => c.Name);

        var items = rows.Select(s =>
        {
            var product = s.Product!;
            var categoryName = product.Category?.Name;
            clinicNameById.TryGetValue(s.ClinicId, out var clinicName);
            clinicName ??= string.Empty;

            return new ProductStockDto(
                s.Id,
                s.ProductId,
                product.Name,
                product.Sku,
                product.ProductCategoryId,
                categoryName,
                s.ClinicId,
                clinicName,
                s.QuantityOnHand,
                s.MinimumStockLevel,
                s.QuantityOnHand < s.MinimumStockLevel,
                s.UpdatedAtUtc);
        }).ToList();

        return Result<PagedResult<ProductStockDto>>.Success(
            PagedResult<ProductStockDto>.Create(items, total, page, pageSize));
    }
}
