using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ProductStocks.Commands.UpdateMinimumStockLevel;

public sealed class UpdateProductStockMinimumStockLevelCommandHandler
    : IRequestHandler<UpdateProductStockMinimumStockLevelCommand, Result<ProductStockDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IRepository<ProductStock> _productStocksWrite;
    private readonly ExpiredStockReader _expiredStock;

    public UpdateProductStockMinimumStockLevelCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Clinic> clinicsRead,
        IRepository<ProductStock> productStocksWrite,
        ExpiredStockReader expiredStock)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _clinicsRead = clinicsRead;
        _productStocksWrite = productStocksWrite;
        _expiredStock = expiredStock;
    }

    public async ValueTask<Result<ProductStockDto>> Handle(
        UpdateProductStockMinimumStockLevelCommand request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ProductStockDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var stock = await _productStocksWrite.FirstOrDefaultAsync(
            new ProductStockForUpdateByIdSpec(tenantId, request.Id),
            ct);
        if (stock is null)
        {
            return Result<ProductStockDto>.Failure(
                "ProductStocks.NotFound",
                "Stok satırı bulunamadı veya kiracıya ait değil.");
        }

        if (_clinicContext.ClinicId.HasValue && stock.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<ProductStockDto>.Failure(
                "ProductStocks.ClinicContextMismatch",
                "Bu stok satırı aktif clinic bağlamı ile uyuşmuyor.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, stock.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<ProductStockDto>.Failure(scopeResult.Error);

        var clinic = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, stock.ClinicId),
            ct);
        if (clinic is null)
        {
            return Result<ProductStockDto>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil.");
        }

        stock.SetMinimumStockLevel(request.MinimumStockLevel);

        try
        {
            await _productStocksWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ProductStockDto>.Failure(
                "ProductStocks.ConcurrencyConflict",
                "Stok satırı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

        var expired = await _expiredStock.GetAsync(tenantId, stock.ClinicId, stock.ProductId, ct);
        return Result<ProductStockDto>.Success(MapDto(stock, clinic.Name, expired));
    }

    private static ProductStockDto MapDto(ProductStock stock, string clinicName, decimal expired)
    {
        var product = stock.Product!;
        var categoryName = product.Category?.Name;
        var available = ExpiredStockReader.Available(stock.QuantityOnHand, expired);

        return new ProductStockDto(
            stock.Id,
            stock.ProductId,
            product.Name,
            product.Sku,
            product.ProductCategoryId,
            categoryName,
            stock.ClinicId,
            clinicName,
            stock.QuantityOnHand,
            stock.MinimumStockLevel,
            available < stock.MinimumStockLevel,
            stock.UpdatedAtUtc,
            available,
            expired);
    }
}
