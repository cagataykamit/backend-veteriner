using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockBatches.Contracts.Dtos;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.StockBatches.Queries.GetProductBalance;

public sealed class GetProductBatchBalanceQueryHandler
    : IRequestHandler<GetProductBatchBalanceQuery, Result<ProductBatchBalanceDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<ProductStock> _stocks;
    private readonly IReadRepository<StockBatch> _batches;
    private readonly TimeProvider _timeProvider;

    public GetProductBatchBalanceQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Product> products,
        IReadRepository<ProductStock> stocks,
        IReadRepository<StockBatch> batches,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _products = products;
        _stocks = stocks;
        _batches = batches;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ProductBatchBalanceDto>> Handle(GetProductBatchBalanceQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ProductBatchBalanceDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, request.ProductId), ct);
        if (product is null)
            return Result<ProductBatchBalanceDto>.Failure("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        var scope = await StockBatchClinicScope.ResolveAsync(
            tenantId, request.ClinicId, _clinicContext, _clinicScopeResolver, ct);
        if (!scope.IsSuccess)
            return Result<ProductBatchBalanceDto>.Failure(scope.Error);

        var clinicId = scope.Value;
        var stock = await _stocks.FirstOrDefaultAsync(
            new ProductStocksForProductReadSpec(tenantId, request.ProductId, clinicId, accessibleClinicIds: null), ct);
        var batches = await _batches.ListAsync(
            new StockBatchesWithBalanceReadSpec(tenantId, clinicId, request.ProductId), ct);

        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        var quantityOnHand = stock?.QuantityOnHand ?? 0;
        var batchedQuantity = batches.Sum(b => b.RemainingQuantity);
        var expiredQuantity = batches.Where(b => b.IsExpiredOn(today)).Sum(b => b.RemainingQuantity);

        return Result<ProductBatchBalanceDto>.Success(new ProductBatchBalanceDto(
            product.Id,
            clinicId,
            product.Unit,
            quantityOnHand,
            batchedQuantity,
            quantityOnHand - batchedQuantity,
            batches.Select(b => b.ToDto(product, today)).ToList(),
            ExpiredStockReader.Available(quantityOnHand, expiredQuantity),
            expiredQuantity));
    }
}
