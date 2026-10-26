using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockBatches.Contracts.Dtos;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.StockBatches.Queries.GetList;

public sealed class GetStockBatchesListQueryHandler
    : IRequestHandler<GetStockBatchesListQuery, Result<PagedResult<StockBatchDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<StockBatch> _batches;
    private readonly IReadRepository<Product> _products;
    private readonly TimeProvider _timeProvider;

    public GetStockBatchesListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<StockBatch> batches,
        IReadRepository<Product> products,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _products = products;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PagedResult<StockBatchDto>>> Handle(GetStockBatchesListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<StockBatchDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var scope = await StockBatchClinicScope.ResolveAsync(
            tenantId, request.ClinicId, _clinicContext, _clinicScopeResolver, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<StockBatchDto>>.Failure(scope.Error);

        var clinicId = scope.Value;
        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        var normalizedLot = ListQueryTextSearch.Normalize(request.LotNumber);
        var lotPattern = normalizedLot is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalizedLot);

        var total = await _batches.CountAsync(
            new StockBatchesFilteredSpec(
                tenantId, clinicId, request.ProductId, lotPattern, request.AvailableOnly, request.Expired, today,
                request.ExpiresBeforeDate),
            ct);
        var rows = await _batches.ListAsync(
            new StockBatchesFilteredPagedSpec(
                tenantId, clinicId, request.ProductId, lotPattern, request.AvailableOnly, request.Expired, today,
                request.ExpiresBeforeDate, request.Page, request.PageSize),
            ct);

        var productIds = rows.Select(r => r.ProductId).Distinct().ToArray();
        var products = productIds.Length == 0
            ? new Dictionary<Guid, Product>()
            : (await _products.ListAsync(new ProductsByIdsSpec(tenantId, productIds), ct)).ToDictionary(p => p.Id);

        var items = rows
            .Where(r => products.ContainsKey(r.ProductId))
            .Select(r => r.ToDto(products[r.ProductId], today))
            .ToList();

        return Result<PagedResult<StockBatchDto>>.Success(
            PagedResult<StockBatchDto>.Create(items, total, request.Page, request.PageSize));
    }
}
