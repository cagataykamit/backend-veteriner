using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Products.Commands.Activate;

public sealed class ActivateProductCommandHandler : IRequestHandler<ActivateProductCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<Product> _products;
    private readonly TimeProvider _timeProvider;

    public ActivateProductCommandHandler(
        ITenantContext tenantContext,
        IRepository<Product> products,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _products = products;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(ActivateProductCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var product = await _products.FirstOrDefaultAsync(new ProductByIdTrackedSpec(tenantId, request.Id), ct);
        if (product is null)
            return Result.Failure("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (!product.IsActive)
        {
            product.Activate(utcNow);
            await _products.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
