using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockBatches.Contracts.Dtos;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.StockBatches.Queries.GetById;

public sealed class GetStockBatchByIdQueryHandler : IRequestHandler<GetStockBatchByIdQuery, Result<StockBatchDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<StockBatch> _batches;
    private readonly IReadRepository<Product> _products;
    private readonly TimeProvider _timeProvider;

    public GetStockBatchByIdQueryHandler(
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

    public async Task<Result<StockBatchDto>> Handle(GetStockBatchByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<StockBatchDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new StockBatchByIdSpec(tenantId, request.Id), ct);
        if (batch is null
            || (_clinicContext.ClinicId is { } contextClinicId && batch.ClinicId != contextClinicId))
        {
            return NotFound();
        }

        // Atanmamış klinikteki kayıt varlığı sızdırmaz: NotFound.
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, batch.ClinicId, ct);
        if (!scope.IsSuccess)
            return NotFound();

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, batch.ProductId), ct);
        if (product is null)
            return NotFound();

        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        return Result<StockBatchDto>.Success(batch.ToDto(product, today));
    }

    private static Result<StockBatchDto> NotFound()
        => Result<StockBatchDto>.Failure("StockBatches.NotFound", "Parti bulunamadı.");
}
