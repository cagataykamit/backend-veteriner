using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.StockMovements.Commands.Create;

public sealed class CreateStockMovementCommandHandler
    : IRequestHandler<CreateStockMovementCommand, Result<StockMovementDto>>
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

    public async Task<Result<StockMovementDto>> Handle(CreateStockMovementCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<StockMovementDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<StockMovementDto>.Failure(
                "StockMovements.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, request.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<StockMovementDto>.Failure(scopeResult.Error);

        var clinic = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, request.ClinicId),
            ct);
        if (clinic is null)
        {
            return Result<StockMovementDto>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil.");
        }

        var product = await _productsRead.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, request.ProductId), ct);
        if (product is null)
        {
            return Result<StockMovementDto>.Failure(
                "Products.NotFound",
                "Ürün bulunamadı veya kiracıya ait değil.");
        }

        if (!product.IsActive)
        {
            return Result<StockMovementDto>.Failure(
                "Products.Inactive",
                "Pasif ürün için stok hareketi oluşturulamaz.");
        }

        string? categoryName = null;
        if (product.ProductCategoryId.HasValue)
        {
            var category = await _categoriesRead.FirstOrDefaultAsync(
                new ProductCategoryByIdSpec(tenantId, product.ProductCategoryId.Value),
                ct);
            categoryName = category?.Name;
        }

        var occurredAtUtc = request.OccurredAtUtc ?? DateTime.UtcNow;
        var createdBy = _clientContext.UserId;
        var referenceId = request.ReferenceId is { } rid && rid != Guid.Empty ? rid : (Guid?)null;

        var stock = await _productStocksWrite.FirstOrDefaultAsync(
            new ProductStockForUpdateSpec(tenantId, request.ClinicId, request.ProductId),
            ct);

        StockMovement movement = default!;

        switch (request.MovementType)
        {
            case StockMovementType.Initial:
                if (stock is not null)
                {
                    return Result<StockMovementDto>.Failure(
                        "StockMovements.StockAlreadyInitialized",
                        "Bu ürün için bu klinikte stok satırı zaten var; Initial kullanılamaz.");
                }

                stock = new ProductStock(tenantId, request.ClinicId, request.ProductId, request.Quantity, 0);
                await _productStocksWrite.AddAsync(stock, ct);
                movement = CreateMovementEntity(
                    tenantId,
                    request,
                    occurredAtUtc,
                    createdBy,
                    referenceId,
                    StockMovementType.Initial,
                    request.Quantity);
                await _stockMovementsWrite.AddAsync(movement, ct);
                break;

            case StockMovementType.In:
                if (stock is null)
                {
                    stock = new ProductStock(tenantId, request.ClinicId, request.ProductId, request.Quantity, 0);
                    await _productStocksWrite.AddAsync(stock, ct);
                }
                else
                    stock.IncreaseQuantity(request.Quantity);

                movement = CreateMovementEntity(
                    tenantId,
                    request,
                    occurredAtUtc,
                    createdBy,
                    referenceId,
                    StockMovementType.In,
                    request.Quantity);
                await _stockMovementsWrite.AddAsync(movement, ct);
                break;

            case StockMovementType.Out:
                if (stock is null || stock.QuantityOnHand < request.Quantity)
                {
                    return Result<StockMovementDto>.Failure(
                        "StockMovements.InsufficientStock",
                        "Çıkış için yeterli stok yok.");
                }

                stock.DecreaseQuantity(request.Quantity);
                movement = CreateMovementEntity(
                    tenantId,
                    request,
                    occurredAtUtc,
                    createdBy,
                    referenceId,
                    StockMovementType.Out,
                    request.Quantity);
                await _stockMovementsWrite.AddAsync(movement, ct);
                break;

            case StockMovementType.Adjustment:
                var oldQty = stock?.QuantityOnHand ?? 0;
                var targetQty = request.Quantity;
                var delta = targetQty - oldQty;
                if (delta == 0)
                {
                    return Result<StockMovementDto>.Failure(
                        "StockMovements.AdjustmentUnchanged",
                        "Hedef stok miktarı mevcut ile aynı; kayıt oluşturulmadı.");
                }

                var movementQty = Math.Abs(delta);
                if (stock is null)
                {
                    stock = new ProductStock(tenantId, request.ClinicId, request.ProductId, targetQty, 0);
                    await _productStocksWrite.AddAsync(stock, ct);
                }
                else
                    stock.SetAbsoluteQuantity(targetQty);

                movement = CreateMovementEntity(
                    tenantId,
                    request,
                    occurredAtUtc,
                    createdBy,
                    referenceId,
                    StockMovementType.Adjustment,
                    movementQty);
                await _stockMovementsWrite.AddAsync(movement, ct);
                break;

            default:
                return Result<StockMovementDto>.Failure(
                    "StockMovements.InvalidMovementType",
                    "Desteklenmeyen hareket tipi.");
        }

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<StockMovementDto>.Failure(
                "StockMovements.ConcurrencyConflict",
                "Stok satırı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

        return Result<StockMovementDto>.Success(
            MapDto(movement, product, categoryName, clinic.Name));
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
            request.Notes);

    private static StockMovementDto MapDto(
        StockMovement movement,
        Product product,
        string? productCategoryName,
        string clinicName)
        => new(
            movement.Id,
            movement.ProductId,
            product.Name,
            product.Sku,
            product.ProductCategoryId,
            productCategoryName,
            movement.ClinicId,
            clinicName,
            movement.MovementType,
            movement.Quantity,
            movement.UnitCost,
            movement.Reason,
            movement.ReferenceType,
            movement.ReferenceId,
            movement.OccurredAtUtc,
            movement.CreatedByUserId,
            movement.Notes,
            movement.CreatedAtUtc);
}
