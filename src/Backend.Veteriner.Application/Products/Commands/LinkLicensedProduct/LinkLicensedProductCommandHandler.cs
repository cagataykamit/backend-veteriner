using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DrugGuide;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Products.Commands.LinkLicensedProduct;

public sealed class LinkLicensedProductCommandHandler : IRequestHandler<LinkLicensedProductCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<Product> _products;
    private readonly IReadRepository<LicensedProduct> _licensed;
    private readonly TimeProvider _timeProvider;

    public LinkLicensedProductCommandHandler(
        ITenantContext tenantContext,
        IRepository<Product> products,
        IReadRepository<LicensedProduct> licensed,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _products = products;
        _licensed = licensed;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(LinkLicensedProductCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var product = await _products.FirstOrDefaultAsync(new ProductByIdTrackedSpec(tenantId, request.ProductId), ct);
        if (product is null)
            return Result.Failure("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        if (request.LicensedProductId is { } licensedId
            && await _licensed.FirstOrDefaultAsync(new LicensedProductsByIdsSpec([licensedId]), ct) is null)
        {
            return Result.Failure(DrugGuideErrors.ProductNotFoundCode, DrugGuideErrors.ProductNotFoundMessage);
        }

        if (product.LicensedProductId == request.LicensedProductId)
            return Result.Success();

        product.LinkLicensedProduct(request.LicensedProductId, _timeProvider.GetUtcNow().UtcDateTime);
        await _products.SaveChangesAsync(ct);
        return Result.Success();
    }
}
