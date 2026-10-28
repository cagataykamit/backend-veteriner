using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;
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
    private readonly StockBatchResolver _batchResolver;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

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
        StockBatchResolver batchResolver,
        IUnitOfWork uow,
        TimeProvider timeProvider)
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
        _batchResolver = batchResolver;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<StockMovementCreateResult>> Handle(ReverseStockMovementCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var original = await _stockMovementsWrite.FirstOrDefaultAsync(
            new StockMovementByIdSpec(tenantId, request.MovementId), ct);
        if (original is null)
            return Fail("StockMovements.NotFound", "Stok hareketi bulunamadı veya kiracıya ait değil.");

        if (ClinicContextRule.Conflicts(original.ClinicId, _clinicContext.ClinicId))
        {
            return Result<StockMovementCreateResult>.Failure(
                ClinicContextRule.Mismatch("StockMovements", "Hareketin kliniği aktif clinic bağlamı ile uyuşmuyor."));
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

        // Pasif ürün ters hareketi engellemez (ADR-013): stok iadesi/geri alma ürün pasif yapıldıktan sonra da yazılabilmelidir.
        var stock = await _productStocksWrite.FirstOrDefaultAsync(
            new ProductStockForUpdateSpec(tenantId, original.ClinicId, original.ProductId), ct);
        if (stock is null && original.ReversalIncreasesStock)
            return Fail("StockMovements.StockNotFound", "Hareketin stok satırı bulunamadı.");

        if (!original.ReversalIncreasesStock && (stock is null || stock.QuantityOnHand < original.Quantity))
            return Fail("StockMovements.InsufficientStock", "Geri alma için yeterli stok yok.");

        // Ters hareket orijinalin partisine uygulanır (null = partisiz bakiye).
        StockBatch? batch = null;
        if (original.BatchId is { } batchId)
        {
            batch = await _batchResolver.FindAsync(tenantId, original.ClinicId, original.ProductId, batchId, ct);
            if (batch is null)
                return Fail("StockBatches.NotFound", "Hareketin partisi bulunamadı.");
        }

        if (!original.ReversalIncreasesStock)
        {
            // Giriş geri alınırken o partinin (partisizse partisiz bakiyenin) kalanı miktarı karşılamalı.
            var available = batch?.RemainingQuantity
                ?? await _batchResolver.GetUnbatchedQuantityAsync(
                    tenantId, original.ClinicId, original.ProductId, stock!.QuantityOnHand, ct);
            if (available < original.Quantity)
                return Fail("StockMovements.InsufficientStock", "Geri alma için yeterli stok yok.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var reversal = original.CreateReversal(
            request.Reason,
            request.Notes,
            utcNow,
            _clientContext.UserId,
            request.RequestId,
            utcNow);

        // Ters hareket tek başına önce yazılır (AddAsync ve hemen ardından SaveChanges): benzersiz kurallar (requestId, tek ters hareket)
        // kaybedeni ilk yazımında eler ve işlemde kaybedenin stok/parti değişikliği kalmaz. Başarısız SaveChanges'in kısmi
        // yazımını geri alacak savepoint'e güvenilmez (EF, MARS açık bağlantıda savepoint açmaz).
        try
        {
            await _stockMovementsWrite.AddAsync(reversal, ct);
            await _stockMovementsWrite.SaveChangesAsync(ct);
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

        if (original.ReversalIncreasesStock)
        {
            batch?.Increase(original.Quantity);
            stock!.IncreaseQuantity(original.Quantity, utcNow);
        }
        else
        {
            batch?.Decrease(original.Quantity);
            stock!.DecreaseQuantity(original.Quantity, utcNow);
        }

        if (batch is not null)
            reversal.LinkBatch(batch); // bakiye, değişiklikten sonraki parti kalanıdır

        // Mevcut satır eşzamanlı değiştiyse (rowVersion) başarısız sonuç işlemi geri alır; ters hareket de kalmaz.
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
