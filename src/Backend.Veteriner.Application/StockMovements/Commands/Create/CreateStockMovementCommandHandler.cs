using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
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

namespace Backend.Veteriner.Application.StockMovements.Commands.Create;

public sealed class CreateStockMovementCommandHandler
    : IRequestHandler<CreateStockMovementCommand, Result<StockMovementCreateResult>>
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
    private readonly IRepository<StockBatch> _batchesWrite;
    private readonly StockBatchResolver _batchResolver;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public CreateStockMovementCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Product> productsRead,
        IReadRepository<ProductCategory> categoriesRead,
        IReadRepository<Clinic> clinicsRead,
        IRepository<ProductStock> productStocksWrite,
        IRepository<StockMovement> stockMovementsWrite,
        IRepository<StockBatch> batchesWrite,
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
        _batchesWrite = batchesWrite;
        _batchResolver = batchResolver;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<StockMovementCreateResult>> Handle(CreateStockMovementCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<StockMovementCreateResult>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<StockMovementCreateResult>.Failure(
                "StockMovements.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, request.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<StockMovementCreateResult>.Failure(scopeResult.Error);

        var clinic = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, request.ClinicId),
            ct);
        if (clinic is null)
        {
            return Result<StockMovementCreateResult>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil.");
        }

        var product = await _productsRead.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, request.ProductId), ct);
        if (product is null)
        {
            return Result<StockMovementCreateResult>.Failure(
                "Products.NotFound",
                "Ürün bulunamadı veya kiracıya ait değil.");
        }

        string? categoryName = null;
        if (product.ProductCategoryId.HasValue)
        {
            var category = await _categoriesRead.FirstOrDefaultAsync(
                new ProductCategoryByIdSpec(tenantId, product.ProductCategoryId.Value),
                ct);
            categoryName = category?.Name;
        }

        // Tekrar istek: aynı RequestId ilk hareketi döndürür, stok değişmez.
        if (request.RequestId is { } requestId && requestId != Guid.Empty)
        {
            var existing = await _stockMovementsWrite.FirstOrDefaultAsync(
                new StockMovementByRequestIdSpec(tenantId, requestId), ct);
            if (existing is not null)
                return Replay(existing, request, product, categoryName, clinic.Name);
        }

        if (!product.IsActive)
        {
            return Result<StockMovementCreateResult>.Failure(
                "Products.Inactive",
                "Pasif ürün için stok hareketi oluşturulamaz.");
        }

        if (!product.IsStockTracked)
        {
            return Result<StockMovementCreateResult>.Failure(
                "Products.NotStockTracked",
                "Hizmet kalemi için stok hareketi oluşturulamaz.");
        }

        var occurredAtUtc = request.OccurredAtUtc ?? _timeProvider.GetUtcNow().UtcDateTime;
        var createdBy = _clientContext.UserId;
        var referenceId = request.ReferenceId is { } rid && rid != Guid.Empty ? rid : (Guid?)null;
        var referenceType = string.IsNullOrWhiteSpace(request.ReferenceType) ? null : request.ReferenceType.Trim();

        // Aynı kaynak, aynı klinik+ürün+tip için ikinci kez stok değiştiremez.
        if (referenceType is not null && referenceId is not null
            && await _stockMovementsWrite.FirstOrDefaultAsync(
                new StockMovementByReferenceSpec(
                    tenantId, request.ClinicId, request.ProductId, referenceType, referenceId.Value, request.MovementType),
                ct) is not null)
        {
            return DuplicateReference();
        }

        var stock = await _productStocksWrite.FirstOrDefaultAsync(
            new ProductStockForUpdateSpec(tenantId, request.ClinicId, request.ProductId),
            ct);

        // Hareket tek başına önce yazılır: benzersiz kuralları (requestId, kaynak) kaybedeni ilk yazımında eler ve işlemde
        // kaybedenin hiçbir değişikliği kalmaz. Yeni stok/parti satırı ve mevcut satırlardaki değişiklik ancak sonra yazılır.
        // Başarısız SaveChanges'in kısmi yazımını geri alacak savepoint'e güvenilmez (EF, MARS açık bağlantıda savepoint açmaz).
        ProductStock? newStock = null;
        Action? changeExistingRows = null; // mevcut stok/parti satırına uygulanacak değişiklik
        StockMovement movement;
        StockBatch? batch = null; // etkilenen parti; null = partisiz (eski/devir) bakiye

        switch (request.MovementType)
        {
            case StockMovementType.Initial:
                if (stock is not null)
                {
                    return Result<StockMovementCreateResult>.Failure(
                        "StockMovements.StockAlreadyInitialized",
                        "Bu ürün için bu klinikte stok satırı zaten var; Initial kullanılamaz.");
                }

                var initialBatch = CreateEntryBatch(tenantId, request, product, createdBy, Today(), _timeProvider.GetUtcNow().UtcDateTime);
                if (!initialBatch.IsSuccess)
                    return Result<StockMovementCreateResult>.Failure(initialBatch.Error);

                batch = initialBatch.Value;
                newStock = new ProductStock(tenantId, request.ClinicId, request.ProductId, request.Quantity, 0);
                movement = CreateMovementEntity(
                    tenantId,
                    request,
                    occurredAtUtc,
                    createdBy,
                    referenceId,
                    StockMovementType.Initial,
                    request.Quantity);
                break;

            case StockMovementType.In:
                var entryBatch = CreateEntryBatch(tenantId, request, product, createdBy, Today(), _timeProvider.GetUtcNow().UtcDateTime);
                if (!entryBatch.IsSuccess)
                    return Result<StockMovementCreateResult>.Failure(entryBatch.Error);

                batch = entryBatch.Value;
                if (stock is null)
                    newStock = new ProductStock(tenantId, request.ClinicId, request.ProductId, request.Quantity, 0);
                else
                    changeExistingRows = () => stock.IncreaseQuantity(request.Quantity);

                movement = CreateMovementEntity(
                    tenantId,
                    request,
                    occurredAtUtc,
                    createdBy,
                    referenceId,
                    StockMovementType.In,
                    request.Quantity);
                break;

            case StockMovementType.Out:
                var onHand = stock?.QuantityOnHand ?? 0;
                var insufficient = Result<StockMovementCreateResult>.Failure(
                    "StockMovements.InsufficientStock",
                    "Çıkış için yeterli stok yok.");

                // Süresi geçmiş partiyi dışlayan çıkışta (uygulama) mesajı çözücü üretir: kullanılabilir/süresi geçmiş miktarı söyler.
                if (onHand < request.Quantity && request.NotExpiredOn is null)
                    return insufficient;

                var consumption = await _batchResolver.ResolveForConsumptionAsync(
                    tenantId,
                    request.ClinicId,
                    request.ProductId,
                    request.BatchId,
                    request.Quantity,
                    onHand,
                    request.NotExpiredOn,
                    ct);
                if (!consumption.IsSuccess)
                    return Result<StockMovementCreateResult>.Failure(consumption.Error);

                if (stock is null || onHand < request.Quantity)
                    return insufficient;

                batch = consumption.Value;
                var consumedBatch = batch;
                changeExistingRows = () =>
                {
                    consumedBatch?.Decrease(request.Quantity);
                    stock.DecreaseQuantity(request.Quantity);
                };
                movement = CreateMovementEntity(
                    tenantId,
                    request,
                    occurredAtUtc,
                    createdBy,
                    referenceId,
                    StockMovementType.Out,
                    request.Quantity);
                break;

            case StockMovementType.Adjustment:
                var oldQty = stock?.QuantityOnHand ?? 0;
                var targetQty = request.Quantity;

                // Parti verilirse hedef o partinin kalanıdır; verilmezse ürünün hedef toplamıdır ve fark partisiz bakiyeye gider.
                if (request.BatchId is { } adjustedBatchId)
                {
                    batch = await _batchResolver.FindAsync(
                        tenantId, request.ClinicId, request.ProductId, adjustedBatchId, ct);
                    if (batch is null || stock is null)
                    {
                        return Result<StockMovementCreateResult>.Failure(
                            StockBatchResolver.NotFoundCode,
                            "Parti bulunamadı veya ürün/klinikle uyuşmuyor.");
                    }

                    oldQty = batch.RemainingQuantity;
                }

                var delta = targetQty - oldQty;
                if (delta == 0)
                {
                    return Result<StockMovementCreateResult>.Failure(
                        "StockMovements.AdjustmentUnchanged",
                        "Hedef stok miktarı mevcut ile aynı; kayıt oluşturulmadı.");
                }

                if (batch is not null)
                {
                    var countedBatch = batch;
                    var countedStock = stock!;
                    changeExistingRows = () =>
                    {
                        countedBatch.SetRemaining(targetQty);
                        countedStock.SetAbsoluteQuantity(countedStock.QuantityOnHand + delta);
                    };
                }
                else
                {
                    if (delta < 0
                        && await _batchResolver.GetUnbatchedQuantityAsync(
                            tenantId, request.ClinicId, request.ProductId, oldQty, ct) < -delta)
                    {
                        return Result<StockMovementCreateResult>.Failure(
                            "StockMovements.BatchRequired",
                            "Eksilme partisiz bakiyeyi aşıyor; düzeltilecek partiyi batchId ile seçin.");
                    }

                    if (stock is null)
                        newStock = new ProductStock(tenantId, request.ClinicId, request.ProductId, targetQty, 0);
                    else
                        changeExistingRows = () => stock.SetAbsoluteQuantity(targetQty);
                }

                movement = CreateMovementEntity(
                    tenantId,
                    request,
                    occurredAtUtc,
                    createdBy,
                    referenceId,
                    StockMovementType.Adjustment,
                    Math.Abs(delta));
                break;

            default:
                return Result<StockMovementCreateResult>.Failure(
                    "StockMovements.InvalidMovementType",
                    "Desteklenmeyen hareket tipi.");
        }

        // Repository AddAsync kaydı hemen kalıcılaştırır (SaveChanges içerir); işlem sınırı TransactionBehavior'dadır.
        try
        {
            await _stockMovementsWrite.AddAsync(movement, ct);
        }
        catch (DbUpdateException)
        {
            // Benzersiz kural kaybedene çarptı; nedeni yeniden sorgulayarak ayırt et. Hiçbir şey yazılmadı.
            if (request.RequestId is { } winnerRequestId && winnerRequestId != Guid.Empty)
            {
                var winner = await _stockMovementsWrite.FirstOrDefaultAsync(
                    new StockMovementByRequestIdSpec(tenantId, winnerRequestId), ct);
                if (winner is not null)
                    return Replay(winner, request, product, categoryName, clinic.Name);
            }

            if (referenceType is not null && referenceId is not null
                && await _stockMovementsWrite.FirstOrDefaultAsync(
                    new StockMovementByReferenceSpec(
                        tenantId, request.ClinicId, request.ProductId, referenceType, referenceId.Value, request.MovementType),
                    ct) is not null)
            {
                return DuplicateReference();
            }

            throw;
        }

        if (newStock is not null)
        {
            try
            {
                await _productStocksWrite.AddAsync(newStock, ct);
            }
            catch (DbUpdateException)
            {
                // Satır yokken eşzamanlı ilk giriş: satırı başka istek oluşturdu; işlem geri alınır, yeniden denenebilir.
                if (await StockRowExistsAsync(tenantId, request.ClinicId, request.ProductId, ct))
                    return ConcurrencyConflict();

                throw;
            }
        }

        if (batch is not null && request.MovementType is StockMovementType.In or StockMovementType.Initial)
            await _batchesWrite.AddAsync(batch, ct);

        // Mevcut satır değişikliği son SaveChanges'e kalır: eşzamanlı değiştiyse (rowVersion) başarısız sonuç işlemi geri alır.
        changeExistingRows?.Invoke();
        if (batch is not null)
            movement.LinkBatch(batch); // bakiye, değişiklikten sonraki parti kalanıdır

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Result<StockMovementCreateResult>.Success(
            new StockMovementCreateResult(movement.ToDto(product, categoryName, clinic.Name), true));
    }

    private DateOnly Today() => OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);

    private async Task<bool> StockRowExistsAsync(Guid tenantId, Guid clinicId, Guid productId, CancellationToken ct)
        => await _productStocksWrite.FirstOrDefaultAsync(
            new ProductStockForUpdateSpec(tenantId, clinicId, productId), ct) is not null;

    private static Result<StockMovementCreateResult> ConcurrencyConflict()
        => Result<StockMovementCreateResult>.Failure(
            "StockMovements.ConcurrencyConflict",
            "Stok satırı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");

    private static Result<StockMovementCreateResult> DuplicateReference()
        => Result<StockMovementCreateResult>.Failure(
            "StockMovements.DuplicateReference",
            "Bu kaynak için aynı ürün, klinik ve hareket tipiyle stok hareketi zaten kayıtlı.");

    private static Result<StockMovementCreateResult> Replay(
        StockMovement existing,
        CreateStockMovementCommand request,
        Product product,
        string? categoryName,
        string clinicName)
    {
        if (existing.ClinicId != request.ClinicId
            || existing.ProductId != request.ProductId
            || existing.MovementType != request.MovementType)
        {
            return Result<StockMovementCreateResult>.Failure(
                "StockMovements.DuplicateRequestId",
                "Bu requestId başka bir stok hareketi isteği için kullanılmış.");
        }

        return Result<StockMovementCreateResult>.Success(
            new StockMovementCreateResult(existing.ToDto(product, categoryName, clinicName), false));
    }

    /// <summary>
    /// Giriş için yeni parti üretir. Parti bilgisi yoksa (veya boşsa) <c>null</c> döner: giriş partisiz bakiyeye eklenir;
    /// lot ve SKT zorunlu üründe bu durum hatadır.
    /// </summary>
    private static Result<StockBatch?> CreateEntryBatch(
        Guid tenantId,
        CreateStockMovementCommand request,
        Product product,
        Guid? createdByUserId,
        DateOnly today,
        DateTime createdAtUtc)
    {
        var details = request.Batch;
        if (details is null || details.IsEmpty)
        {
            return product.RequiresLotAndExpiry
                ? Result<StockBatch?>.Failure(
                    StockBatch.LotAndExpiryRequiredCode,
                    "Aşı ve kontrollü ürünlerde seri/lot no ve son kullanma tarihi zorunludur.")
                : Result<StockBatch?>.Success(null);
        }

        // Geçmiş SKT engellenmez ama açık onay ister (ADR-015 karar 6).
        if (details.ExpiryDate is { } expiry && expiry < today && request.AcknowledgeExpired != true)
        {
            return Result<StockBatch?>.Failure(
                StockBatch.ExpiredAcknowledgementRequiredCode,
                "Bu partinin son kullanma tarihi geçmiş. Devam etmek için onaylayın.");
        }

        var created = StockBatch.Create(
            tenantId, request.ClinicId, product, request.Quantity, details, createdByUserId, createdAtUtc);
        return created.IsSuccess
            ? Result<StockBatch?>.Success(created.Value)
            : Result<StockBatch?>.Failure(created.Error);
    }

    private static StockMovement CreateMovementEntity(
        Guid tenantId,
        CreateStockMovementCommand request,
        DateTime occurredAtUtc,
        Guid? createdByUserId,
        Guid? referenceId,
        StockMovementType type,
        decimal movementQuantity)
        => new(
            tenantId,
            request.ClinicId,
            request.ProductId,
            type,
            movementQuantity,
            occurredAtUtc,
            request.UnitCost,
            request.Reason,
            request.ReferenceType,
            referenceId,
            createdByUserId,
            request.Notes,
            request.RequestId);
}
