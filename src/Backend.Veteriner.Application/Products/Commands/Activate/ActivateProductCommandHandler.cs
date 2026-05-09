using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Commands.Activate;

public sealed class ActivateProductCommandHandler : IRequestHandler<ActivateProductCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<Product> _products;

    public ActivateProductCommandHandler(
        ITenantContext tenantContext,
        IRepository<Product> products)
    {
        _tenantContext = tenantContext;
        _products = products;
    }

    public async Task<Result> Handle(ActivateProductCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var product = await _products.FirstOrDefaultAsync(new ProductByIdTrackedSpec(tenantId, request.Id), ct);
        if (product is null)
            return Result.Failure("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        if (!product.IsActive)
        {
            product.Activate();
            await _products.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
