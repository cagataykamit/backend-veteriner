using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Commands.Update;

public sealed class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Product> _productsRead;
    private readonly IRepository<Product> _productsWrite;
    private readonly IReadRepository<ProductCategory> _categoriesRead;

    public UpdateProductCommandHandler(
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

    public async Task<Result> Handle(UpdateProductCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var product = await _productsWrite.FirstOrDefaultAsync(new ProductByIdTrackedSpec(tenantId, request.Id), ct);
        if (product is null)
            return Result.Failure("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        if (request.ProductCategoryId.HasValue)
        {
            var category = await _categoriesRead.FirstOrDefaultAsync(
                new ProductCategoryByIdSpec(tenantId, request.ProductCategoryId.Value),
                ct);
            if (category is null)
                return Result.Failure("Products.CategoryNotFound", "Kategori bulunamadı veya kiracıya ait değil.");
            if (!category.IsActive)
                return Result.Failure("Products.CategoryInactive", "Pasif kategoriye ürün bağlanamaz.");
        }

        var normalizedSku = string.IsNullOrWhiteSpace(request.Sku)
            ? null
            : request.Sku.Trim().ToLowerInvariant();

        if (normalizedSku is not null)
        {
            var duplicate = await _productsRead.FirstOrDefaultAsync(
                new ProductByTenantAndSkuSpec(tenantId, normalizedSku, request.Id),
                ct);
            if (duplicate is not null)
                return Result.Failure("Products.SkuAlreadyExists", "Bu kiracıda aynı SKU ile başka ürün var.");
        }

        product.Update(
            request.ProductCategoryId,
            request.Name,
            request.Unit,
            request.UnitPrice,
            request.Currency,
            request.Sku,
            request.Barcode,
            request.Description);

        await _productsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
