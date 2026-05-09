using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Deactivate;

public sealed class DeactivateProductCategoryCommandHandler : IRequestHandler<DeactivateProductCategoryCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ProductCategory> _categories;

    public DeactivateProductCategoryCommandHandler(
        ITenantContext tenantContext,
        IRepository<ProductCategory> categories)
    {
        _tenantContext = tenantContext;
        _categories = categories;
    }

    public async Task<Result> Handle(DeactivateProductCategoryCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var category = await _categories.FirstOrDefaultAsync(new ProductCategoryByIdSpec(tenantId, request.Id), ct);
        if (category is null)
            return Result.Failure("ProductCategories.NotFound", "Kategori bulunamadı veya kiracıya ait değil.");

        if (category.IsActive)
        {
            category.Deactivate();
            await _categories.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
