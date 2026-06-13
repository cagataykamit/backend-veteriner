using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductStocks.Queries.GetByProductId;

public sealed class GetProductStocksByProductIdQueryHandler
    : IRequestHandler<GetProductStocksByProductIdQuery, Result<IReadOnlyList<ProductStockDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<ProductCategory> _categories;
    private readonly IReadRepository<ProductStock> _productStocks;
    private readonly IReadRepository<Clinic> _clinics;

    public GetProductStocksByProductIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Product> products,
        IReadRepository<ProductCategory> categories,
        IReadRepository<ProductStock> productStocks,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _products = products;
        _categories = categories;
        _productStocks = productStocks;
        _clinics = clinics;
    }

    public async Task<Result<IReadOnlyList<ProductStockDto>>> Handle(
        GetProductStocksByProductIdQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ProductStockDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, request.ProductId), ct);
        if (product is null)
        {
            return Result<IReadOnlyList<ProductStockDto>>.Failure(
                "Products.NotFound",
                "Ürün bulunamadı veya kiracıya ait değil.");
        }

        var requestedClinicId = _clinicContext.ClinicId;

        if (requestedClinicId is null)
        {
            return Result<IReadOnlyList<ProductStockDto>>.Failure(
                "ProductStocks.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok. Ürün stokları klinik kapsamı olmadan listelenemez.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<IReadOnlyList<ProductStockDto>>.Failure(scopeResult.Error);

        string? categoryName = null;
        if (product.ProductCategoryId.HasValue)
        {
            var category = await _categories.FirstOrDefaultAsync(
                new ProductCategoryByIdSpec(tenantId, product.ProductCategoryId.Value),
                ct);
            categoryName = category?.Name;
        }

        var effectiveClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value!.AccessibleClinicIds;

        var rows = await _productStocks.ListAsync(
            new ProductStocksForProductReadSpec(
                tenantId,
                request.ProductId,
                effectiveClinicId,
                accessibleClinicIds),
            ct);

        var clinicIds = rows.Select(r => r.ClinicId).Distinct().ToArray();
        var clinics = clinicIds.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantAndIdsSpec(tenantId, clinicIds), ct);
        var clinicNameById = clinics.ToDictionary(c => c.Id, c => c.Name);

        var items = rows.Select(s =>
        {
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

        return Result<IReadOnlyList<ProductStockDto>>.Success(items);
    }
}
