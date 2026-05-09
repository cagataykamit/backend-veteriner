using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Commands.Create;

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<ProductDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Product> _productsRead;
    private readonly IRepository<Product> _productsWrite;
    private readonly IReadRepository<ProductCategory> _categoriesRead;

    public CreateProductCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Product> productsRead,
        IRepository<Product> productsWrite,
        IReadRepository<ProductCategory> categoriesRead)
    {
        _tenantContext = tenantContext;
        _productsRead = productsRead;
        _productsWrite = productsWrite;
        _categoriesRead = categoriesRead;
    }

    public async Task<Result<ProductDto>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ProductDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        ProductCategory? category = null;
        if (request.ProductCategoryId.HasValue)
        {
            category = await _categoriesRead.FirstOrDefaultAsync(
                new ProductCategoryByIdSpec(tenantId, request.ProductCategoryId.Value),
                ct);
            if (category is null)
                return Result<ProductDto>.Failure("Products.CategoryNotFound", "Kategori bulunamadı veya kiracıya ait değil.");
            if (!category.IsActive)
                return Result<ProductDto>.Failure("Products.CategoryInactive", "Pasif kategoriye ürün bağlanamaz.");
        }

        var normalizedSku = string.IsNullOrWhiteSpace(request.Sku)
            ? null
            : request.Sku.Trim().ToLowerInvariant();

        if (normalizedSku is not null)
        {
            var duplicate = await _productsRead.FirstOrDefaultAsync(
                new ProductByTenantAndSkuSpec(tenantId, normalizedSku),
                ct);
            if (duplicate is not null)
                return Result<ProductDto>.Failure("Products.SkuAlreadyExists", "Bu kiracıda aynı SKU ile başka ürün var.");
        }

        var product = new Product(
            tenantId,
            request.Name,
            request.Unit,
            request.UnitPrice,
            request.Currency,
            request.ProductCategoryId,
            request.Sku,
            request.Barcode,
            request.Description);

        await _productsWrite.AddAsync(product, ct);
        await _productsWrite.SaveChangesAsync(ct);

        return Result<ProductDto>.Success(ToDto(product, category?.Name));
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
