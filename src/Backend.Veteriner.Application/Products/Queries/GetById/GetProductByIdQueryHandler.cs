using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Queries.GetById;

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<ProductCategory> _categories;

    public GetProductByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Product> products,
        IReadRepository<ProductCategory> categories)
    {
        _tenantContext = tenantContext;
        _products = products;
        _categories = categories;
    }

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ProductDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, request.Id), ct);
        if (product is null)
            return Result<ProductDto>.Failure("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        string? categoryName = null;
        if (product.ProductCategoryId.HasValue)
        {
            var category = await _categories.FirstOrDefaultAsync(
                new ProductCategoryByIdSpec(tenantId, product.ProductCategoryId.Value),
                ct);
            categoryName = category?.Name;
        }

        return Result<ProductDto>.Success(ToDto(product, categoryName));
    }

    private static ProductDto ToDto(Product p, string? categoryName)
        => new(
            p.Id,
            p.ProductCategoryId,
            categoryName,
            p.Name,
            p.Sku,
            p.Barcode,
            p.Description,
            p.Unit,
            p.UnitPrice,
            p.Currency,
            p.IsActive);
}
