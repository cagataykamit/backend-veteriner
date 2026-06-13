using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ProductCategories.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.StockMovements.Queries.GetByProductId;

public sealed class GetStockMovementsByProductIdQueryHandler
    : IRequestHandler<GetStockMovementsByProductIdQuery, Result<PagedResult<StockMovementDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<ProductCategory> _categories;
    private readonly IReadRepository<StockMovement> _stockMovements;
    private readonly IReadRepository<Clinic> _clinics;

    public GetStockMovementsByProductIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Product> products,
        IReadRepository<ProductCategory> categories,
        IReadRepository<StockMovement> stockMovements,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _products = products;
        _categories = categories;
        _stockMovements = stockMovements;
        _clinics = clinics;
    }

    public async Task<Result<PagedResult<StockMovementDto>>> Handle(
        GetStockMovementsByProductIdQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<StockMovementDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, request.ProductId), ct);
        if (product is null)
        {
            return Result<PagedResult<StockMovementDto>>.Failure(
                "Products.NotFound",
                "Ürün bulunamadı veya kiracıya ait değil.");
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue &&
            request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<StockMovementDto>>.Failure(
                "StockMovements.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;

        if (requestedClinicId is null)
        {
            return Result<PagedResult<StockMovementDto>>.Failure(
                "StockMovements.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi. Stok hareketleri klinik kapsamı olmadan listelenemez.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PagedResult<StockMovementDto>>.Failure(scopeResult.Error);

        string? categoryName = null;
        if (product.ProductCategoryId.HasValue)
        {
            var category = await _categories.FirstOrDefaultAsync(
                new ProductCategoryByIdSpec(tenantId, product.ProductCategoryId.Value),
                ct);
            categoryName = category?.Name;
        }

        var effectiveClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value!.AccessibleClinicIds;

        var total = await _stockMovements.CountAsync(
            new StockMovementsForProductFilteredCountSpec(
                tenantId,
                request.ProductId,
                effectiveClinicId,
                accessibleClinicIds,
                request.MovementType,
                request.DateFromUtc,
                request.DateToUtc),
            ct);

        var rows = await _stockMovements.ListAsync(
            new StockMovementsForProductFilteredPagedSpec(
                tenantId,
                request.ProductId,
                effectiveClinicId,
                accessibleClinicIds,
                request.MovementType,
                request.DateFromUtc,
                request.DateToUtc,
                page,
                pageSize),
            ct);

        var clinicIds = rows.Select(r => r.ClinicId).Distinct().ToArray();
        var clinics = clinicIds.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantAndIdsSpec(tenantId, clinicIds), ct);
        var clinicNameById = clinics.ToDictionary(c => c.Id, c => c.Name);

        var items = rows.Select(m =>
        {
            clinicNameById.TryGetValue(m.ClinicId, out var clinicName);
            clinicName ??= string.Empty;

            return new StockMovementDto(
                m.Id,
                m.ProductId,
                product.Name,
                product.Sku,
                product.ProductCategoryId,
                categoryName,
                m.ClinicId,
                clinicName,
                m.MovementType,
                m.Quantity,
                m.UnitCost,
                m.Reason,
                m.ReferenceType,
                m.ReferenceId,
                m.OccurredAtUtc,
                m.CreatedByUserId,
                m.Notes,
                m.CreatedAtUtc);
        }).ToList();

        return Result<PagedResult<StockMovementDto>>.Success(
            PagedResult<StockMovementDto>.Create(items, total, page, pageSize));
    }
}
