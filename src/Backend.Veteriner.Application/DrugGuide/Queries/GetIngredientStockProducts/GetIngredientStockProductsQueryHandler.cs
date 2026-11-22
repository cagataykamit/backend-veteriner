using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetIngredientStockProducts;

public sealed class GetIngredientStockProductsQueryHandler
    : IRequestHandler<GetIngredientStockProductsQuery, Result<IReadOnlyList<IngredientStockProductDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ActiveIngredient> _ingredients;
    private readonly IReadRepository<LicensedProductIngredient> _links;
    private readonly IReadRepository<LicensedProduct> _licensed;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<ProductStock> _stocks;

    public GetIngredientStockProductsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ActiveIngredient> ingredients,
        IReadRepository<LicensedProductIngredient> links,
        IReadRepository<LicensedProduct> licensed,
        IReadRepository<Product> products,
        IReadRepository<ProductStock> stocks)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _ingredients = ingredients;
        _links = links;
        _licensed = licensed;
        _products = products;
        _stocks = stocks;
    }

    public async ValueTask<Result<IReadOnlyList<IngredientStockProductDto>>> Handle(GetIngredientStockProductsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<IReadOnlyList<IngredientStockProductDto>>.Failure(ClinicContextRule.Mismatch("DrugGuide"));

        // Stok klinik bazlıdır: tek klinik kapsamı gerekir.
        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (requestedClinicId is null)
            return Fail("DrugGuide.ClinicScopeRequired", "Stok için aktif klinik ya da clinicId gerekir.");

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<IngredientStockProductDto>>.Failure(scope.Error);
        var clinicId = scope.Value!.SingleClinicId ?? requestedClinicId.Value;

        if (await _ingredients.FirstOrDefaultAsync(new ActiveIngredientByIdSpec(request.ActiveIngredientId), ct) is null)
            return Fail(DrugGuideErrors.IngredientNotFoundCode, DrugGuideErrors.IngredientNotFoundMessage);

        var licensedIds = (await _links.ListAsync(new LicensedProductIngredientsByIngredientSpec([request.ActiveIngredientId]), ct))
            .Select(l => l.LicensedProductId)
            .Distinct()
            .ToList();
        if (licensedIds.Count == 0)
            return Result<IReadOnlyList<IngredientStockProductDto>>.Success([]);

        var products = await _products.ListAsync(new ProductsLinkedToLicensedProductsSpec(tenantId, licensedIds), ct);
        if (products.Count == 0)
            return Result<IReadOnlyList<IngredientStockProductDto>>.Success([]);

        var licensedNames = (await _licensed.ListAsync(new LicensedProductsByIdsSpec(products.Select(p => p.LicensedProductId!.Value).Distinct().ToList()), ct))
            .ToDictionary(l => l.Id, l => l.Name);
        var quantities = (await _stocks.ListAsync(new ProductStocksInClinicSpec(tenantId, clinicId, products.Select(p => p.Id).ToList()), ct))
            .ToDictionary(s => s.ProductId, s => s.QuantityOnHand);

        IReadOnlyList<IngredientStockProductDto> result = products
            .Select(p => new IngredientStockProductDto(
                p.Id,
                p.Name,
                p.Unit,
                p.IsActive,
                p.LicensedProductId!.Value,
                licensedNames.GetValueOrDefault(p.LicensedProductId!.Value, string.Empty),
                quantities.GetValueOrDefault(p.Id)))
            .ToList();
        return Result<IReadOnlyList<IngredientStockProductDto>>.Success(result);
    }

    private static Result<IReadOnlyList<IngredientStockProductDto>> Fail(string code, string message)
        => Result<IReadOnlyList<IngredientStockProductDto>>.Failure(code, message);
}
