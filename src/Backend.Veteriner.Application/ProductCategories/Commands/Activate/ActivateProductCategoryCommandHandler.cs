using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Activate;

public sealed class ActivateProductCategoryCommandHandler : IRequestHandler<ActivateProductCategoryCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ProductCategory> _categories;

    public ActivateProductCategoryCommandHandler(
        ITenantContext tenantContext,
        IRepository<ProductCategory> categories)
    {
        _tenantContext = tenantContext;
        _categories = categories;
    }

    public async Task<Result> Handle(ActivateProductCategoryCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var category = await _categories.FirstOrDefaultAsync(new ProductCategoryByIdSpec(tenantId, request.Id), ct);
        if (category is null)
            return Result.Failure("ProductCategories.NotFound", "Kategori bulunamadı veya kiracıya ait değil.");

        if (!category.IsActive)
        {
            category.Activate();
            await _categories.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
