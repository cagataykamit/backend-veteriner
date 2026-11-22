using Ardalis.Specification;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.DrugGuide.Specs;

/// <summary>Rehber aramasında hangi alanın aranacağı; <see cref="All"/> ürün adı, etken madde ve ruhsat sahibi.</summary>
public enum LicensedProductSearchField
{
    All = 0,
    Product = 1,
    Ingredient = 2,
    Holder = 3
}

/// <summary>
/// Rehber araması (tek kural): her arama parçası seçilen alanlardan birinin Türkçe anahtarında geçmelidir.
/// <c>activeIngredientId</c> verilirse yalnız o maddeyi içeren ürünler.
/// </summary>
internal static class LicensedProductSearch
{
    public static void Apply(
        ISpecificationBuilder<LicensedProduct> query,
        IReadOnlyList<string> tokenPatterns,
        LicensedProductSearchField field,
        Guid? activeIngredientId)
    {
        if (activeIngredientId is { } ingredientId)
            query.Where(x => x.Ingredients.Any(i => i.ActiveIngredientId == ingredientId));

        foreach (var pattern in tokenPatterns)
        {
            switch (field)
            {
                case LicensedProductSearchField.Product:
                    query.Where(x => EF.Functions.Like(x.NormalizedName, pattern));
                    break;
                case LicensedProductSearchField.Ingredient:
                    query.Where(x => x.Ingredients.Any(i => EF.Functions.Like(i.Ingredient!.NormalizedName, pattern)));
                    break;
                case LicensedProductSearchField.Holder:
                    query.Where(x => EF.Functions.Like(x.NormalizedLicenseHolder, pattern));
                    break;
                default:
                    query.Where(x => EF.Functions.Like(x.NormalizedName, pattern)
                                     || EF.Functions.Like(x.NormalizedLicenseHolder, pattern)
                                     || x.Ingredients.Any(i => EF.Functions.Like(i.Ingredient!.NormalizedName, pattern)));
                    break;
            }
        }
    }
}

public sealed class LicensedProductsCountSpec : Specification<LicensedProduct>
{
    public LicensedProductsCountSpec(IReadOnlyList<string> tokenPatterns, LicensedProductSearchField field, Guid? activeIngredientId)
        => LicensedProductSearch.Apply(Query, tokenPatterns, field, activeIngredientId);
}

/// <summary>Kaynakta hâlâ listelenenler önce, sonra ada göre.</summary>
public sealed class LicensedProductsPagedSpec : Specification<LicensedProduct>
{
    public LicensedProductsPagedSpec(
        IReadOnlyList<string> tokenPatterns,
        LicensedProductSearchField field,
        Guid? activeIngredientId,
        int page,
        int pageSize)
    {
        LicensedProductSearch.Apply(Query, tokenPatterns, field, activeIngredientId);
        Query.Include(x => x.Ingredients).ThenInclude(i => i.Ingredient);
        Query.Include(x => x.TargetSpecies);
        Query.OrderByDescending(x => x.IsListedInSource).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .AsNoTracking();
    }
}

public sealed class LicensedProductsByIdsSpec : Specification<LicensedProduct>
{
    public LicensedProductsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        var idArray = ids.ToArray();
        Query.Where(x => idArray.Contains(x.Id));
        Query.Include(x => x.Ingredients).ThenInclude(i => i.Ingredient);
        Query.Include(x => x.TargetSpecies);
        Query.AsSplitQuery().AsNoTracking();
    }
}

/// <summary>Kaynağın en son eşitleme tarihi için (özet ucu).</summary>
public sealed class LicensedProductLatestSyncSpec : Specification<LicensedProduct, DateTime>
{
    public LicensedProductLatestSyncSpec()
    {
        Query.OrderByDescending(x => x.SourceSyncedAtUtc).Take(1);
        Query.Select(x => x.SourceSyncedAtUtc);
    }
}

/// <summary>Etken madde araması: Türkçe anahtarda her parça geçmeli; ada göre.</summary>
public sealed class ActiveIngredientsSearchSpec : Specification<ActiveIngredient>
{
    public ActiveIngredientsSearchSpec(IReadOnlyList<string> tokenPatterns, int take)
    {
        foreach (var pattern in tokenPatterns)
            Query.Where(x => EF.Functions.Like(x.NormalizedName, pattern));
        Query.OrderBy(x => x.Name).Take(take).AsNoTracking();
    }
}

public sealed class ActiveIngredientByIdSpec : Specification<ActiveIngredient>
{
    public ActiveIngredientByIdSpec(Guid id) => Query.Where(x => x.Id == id).AsNoTracking();
}

/// <summary>Verilen etken maddeleri içeren ürün bağları (sayım ve stok listesi için).</summary>
public sealed class LicensedProductIngredientsByIngredientSpec : Specification<LicensedProductIngredient>
{
    public LicensedProductIngredientsByIngredientSpec(IReadOnlyCollection<Guid> activeIngredientIds)
    {
        var ids = activeIngredientIds.ToArray();
        Query.Where(x => ids.Contains(x.ActiveIngredientId)).AsNoTracking();
    }
}

/// <summary>Kiracının rehbere bağlı ürün kartları (verilen ruhsatlı ürünlerden birine bağlı olanlar).</summary>
public sealed class ProductsLinkedToLicensedProductsSpec : Specification<Product>
{
    public ProductsLinkedToLicensedProductsSpec(Guid tenantId, IReadOnlyCollection<Guid> licensedProductIds)
    {
        var ids = licensedProductIds.ToArray();
        Query.Where(x => x.TenantId == tenantId && x.LicensedProductId != null && ids.Contains(x.LicensedProductId.Value))
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .AsNoTracking();
    }
}

/// <summary>Ürünlerin tek klinikteki stok satırları.</summary>
public sealed class ProductStocksInClinicSpec : Specification<ProductStock>
{
    public ProductStocksInClinicSpec(Guid tenantId, Guid clinicId, IReadOnlyCollection<Guid> productIds)
    {
        var ids = productIds.ToArray();
        Query.Where(s => s.TenantId == tenantId && s.ClinicId == clinicId && ids.Contains(s.ProductId)).AsNoTracking();
    }
}
