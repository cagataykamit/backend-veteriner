using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Update;

public sealed class UpdateProductCategoryCommandHandler : IRequestHandler<UpdateProductCategoryCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ProductCategory> _categoriesRead;
    private readonly IRepository<ProductCategory> _categoriesWrite;
    private readonly TimeProvider _timeProvider;

    public UpdateProductCategoryCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<ProductCategory> categoriesRead,
        IRepository<ProductCategory> categoriesWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _categoriesRead = categoriesRead;
        _categoriesWrite = categoriesWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(UpdateProductCategoryCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var category = await _categoriesWrite.FirstOrDefaultAsync(new ProductCategoryByIdSpec(tenantId, request.Id), ct);
        if (category is null)
            return Result.Failure("ProductCategories.NotFound", "Kategori bulunamadı veya kiracıya ait değil.");

        var trimmedName = request.Name.Trim();
        var duplicate = await _categoriesRead.FirstOrDefaultAsync(
            new ProductCategoryByTenantAndNameSpec(tenantId, trimmedName, request.Id),
            ct);

        if (duplicate is not null)
            return Result.Failure("ProductCategories.NameAlreadyExists", "Bu kiracı için aynı kategori adı zaten mevcut.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        category.Update(request.Name, request.Description, utcNow);
        await _categoriesWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
