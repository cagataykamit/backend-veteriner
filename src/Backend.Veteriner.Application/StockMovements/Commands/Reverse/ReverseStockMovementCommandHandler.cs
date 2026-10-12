using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.StockMovements.Commands.Reverse;

public sealed class ReverseStockMovementCommandHandler
    : IRequestHandler<ReverseStockMovementCommand, Result<StockMovementCreateResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Product> _productsRead;
    private readonly IReadRepository<ProductCategory> _categoriesRead;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IRepository<ProductStock> _productStocksWrite;
    private readonly IRepository<StockMovement> _stockMovementsWrite;
    private readonly IUnitOfWork _uow;

    public ReverseStockMovementCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Product> productsRead,
        IReadRepository<ProductCategory> categoriesRead,
        IReadRepository<Clinic> clinicsRead,
        IRepository<ProductStock> productStocksWrite,
        IRepository<StockMovement> stockMovementsWrite,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _productsRead = productsRead;
        _categoriesRead = categoriesRead;
        _clinicsRead = clinicsRead;
        _productStocksWrite = productStocksWrite;
        _stockMovementsWrite = stockMovementsWrite;
        _uow = uow;
    }

    public async Task<Result<StockMovementCreateResult>> Handle(ReverseStockMovementCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var original = await _stockMovementsWrite.FirstOrDefaultAsync(
            new StockMovementByIdSpec(tenantId, request.MovementId), ct);
        if (original is null)
            return Fail("StockMovements.NotFound", "Stok hareketi bulunamadı veya kiracıya ait değil.");

        if (_clinicContext.ClinicId.HasValue && original.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Fail(
                "StockMovements.ClinicContextMismatch",
                "Hareketin kliniği aktif clinic bağlamı ile uyuşmuyor.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, original.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<StockMovementCreateResult>.Failure(scopeResult.Error);

        var clinic = await _clinicsRead.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, original.ClinicId), ct);
        if (clinic is null)
            return Fail("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var product = await _productsRead.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, original.ProductId), ct);
        if (product is null)
            return Fail("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        string? categoryName = null;
        if (product.ProductCategoryId.HasValue)
        {
            var category = await _categoriesRead.FirstOrDefaultAsync(
                new ProductCategoryByIdSpec(tenantId, product.ProductCategoryId.Value), ct);
            categoryName = category?.Name;
        }

        // Tekrar istek: aynı RequestId ilk ters hareketi döndürür, stok değişmez.
        if (request.RequestId is { } requestId && requestId != Guid.Empty)
        {
            var existing = await _stockMovementsWrite.FirstOrDefaultAsync(
                new StockMovementByRequestIdSpec(tenantId, requestId), ct);
            if (existing is not null)
                return Replay(existing, original, product, categoryName, clinic.Name);
        }

        if (!original.IsReversible)
            return Fail("StockMovements.NotReversible", "Bu hareket tipi geri alınamaz.");

        if (await _stockMovementsWrite.FirstOrDefaultAsync(
                new StockMovementReversalOfSpec(tenantId, original.Id), ct) is not null)
        {
            return AlreadyReversed();
        }

        if (!product.IsActive)
            return Fail("Products.Inactive", "Pasif ürün için stok hareketi oluşturulamaz.");

        var stock = await _productStocksWrite.FirstOrDefaultAsync(
            new ProductStockForUpdateSpec(tenantId, original.ClinicId, original.ProductId), ct);
        if (stock is null && original.ReversalIncreasesStock)
            return Fail("StockMovements.StockNotFound", "Hareketin stok satırı bulunamadı.");

        if (original.ReversalIncreasesStock)
            stock!.IncreaseQuantity(original.Quantity);
        else if (stock is null || stock.QuantityOnHand < original.Quantity)
            return Fail("StockMovements.InsufficientStock", "Geri alma için yeterli stok yok.");
        else
            stock.DecreaseQuantity(original.Quantity);

        var reversal = original.CreateReversal(
            request.Reason,
            request.Notes,
            DateTime.UtcNow,
            _clientContext.UserId,
            request.RequestId);

        // Repository AddAsync kaydı hemen kalıcılaştırır (SaveChanges içerir): ters hareket ve stok değişimi tek toplu yazımdır.
        try
        {
            await _stockMovementsWrite.AddAsync(reversal, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }
        catch (DbUpdateException)
        {
            // Benzersiz kural kaybedene çarptı; nedeni yeniden sorgulayarak ayırt et. Hiçbir şey yazılmadı.
            if (request.RequestId is { } winnerRequestId && winnerRequestId != Guid.Empty)
            {
                var winner = await _stockMovementsWrite.FirstOrDefaultAsync(
                    new StockMovementByRequestIdSpec(tenantId, winnerRequestId), ct);
                if (winner is not null)
                    return Replay(winner, original, product, categoryName, clinic.Name);
            }

            if (await _stockMovementsWrite.FirstOrDefaultAsync(
                    new StockMovementReversalOfSpec(tenantId, original.Id), ct) is not null)
            {
                return AlreadyReversed();
            }

            throw;
        }

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Result<StockMovementCreateResult>.Success(
            new StockMovementCreateResult(reversal.ToDto(product, categoryName, clinic.Name), true));
    }

    private static Result<StockMovementCreateResult> Replay(
        StockMovement existing,
        StockMovement original,
        Product product,
        string? categoryName,
        string clinicName)
    {
        if (existing.ReversesMovementId != original.Id)
        {
            return Fail(
                "StockMovements.DuplicateRequestId",
                "Bu requestId başka bir stok hareketi isteği için kullanılmış.");
        }

        return Result<StockMovementCreateResult>.Success(
            new StockMovementCreateResult(existing.ToDto(product, categoryName, clinicName), false));
    }

    private static Result<StockMovementCreateResult> AlreadyReversed()
        => Fail("StockMovements.AlreadyReversed", "Bu hareket zaten geri alınmış.");

    private static Result<StockMovementCreateResult> ConcurrencyConflict()
        => Fail(
            "StockMovements.ConcurrencyConflict",
            "Stok satırı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");

    private static Result<StockMovementCreateResult> Fail(string code, string message)
        => Result<StockMovementCreateResult>.Failure(code, message);
}
