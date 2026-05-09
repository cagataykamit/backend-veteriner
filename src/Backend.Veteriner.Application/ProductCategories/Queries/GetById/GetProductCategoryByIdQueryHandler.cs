using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Contracts.Dtos;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Queries.GetById;

public sealed class GetProductCategoryByIdQueryHandler
    : IRequestHandler<GetProductCategoryByIdQuery, Result<ProductCategoryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ProductCategory> _categories;

    public GetProductCategoryByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<ProductCategory> categories)
    {
        _tenantContext = tenantContext;
        _categories = categories;
    }

    public async Task<Result<ProductCategoryDto>> Handle(GetProductCategoryByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ProductCategoryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var category = await _categories.FirstOrDefaultAsync(new ProductCategoryByIdSpec(tenantId, request.Id), ct);
        if (category is null)
            return Result<ProductCategoryDto>.Failure(
                "ProductCategories.NotFound",
                "Kategori bulunamadı veya kiracıya ait değil.");

        return Result<ProductCategoryDto>.Success(
            new ProductCategoryDto(category.Id, category.Name, category.Description, category.IsActive));
    }
}
