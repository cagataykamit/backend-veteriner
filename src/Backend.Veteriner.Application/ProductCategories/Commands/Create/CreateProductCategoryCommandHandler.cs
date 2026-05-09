using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Contracts.Dtos;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Create;

public sealed class CreateProductCategoryCommandHandler
    : IRequestHandler<CreateProductCategoryCommand, Result<ProductCategoryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ProductCategory> _categoriesRead;
    private readonly IRepository<ProductCategory> _categoriesWrite;

    public CreateProductCategoryCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<ProductCategory> categoriesRead,
        IRepository<ProductCategory> categoriesWrite)
    {
        _tenantContext = tenantContext;
        _categoriesRead = categoriesRead;
        _categoriesWrite = categoriesWrite;
    }

    public async Task<Result<ProductCategoryDto>> Handle(CreateProductCategoryCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ProductCategoryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var normalizedName = request.Name.Trim().ToLowerInvariant();
        var duplicate = await _categoriesRead.FirstOrDefaultAsync(
            new ProductCategoryByTenantAndNameSpec(tenantId, normalizedName),
            ct);

        if (duplicate is not null)
            return Result<ProductCategoryDto>.Failure(
                "ProductCategories.NameAlreadyExists",
                "Bu kiracı için aynı kategori adı zaten mevcut.");

        var category = new ProductCategory(tenantId, request.Name, request.Description);
        await _categoriesWrite.AddAsync(category, ct);
        await _categoriesWrite.SaveChangesAsync(ct);

        return Result<ProductCategoryDto>.Success(
            new ProductCategoryDto(category.Id, category.Name, category.Description, category.IsActive));
    }
}
