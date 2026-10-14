using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.ExaminationTemplates;

/// <summary>
/// Şablon satırlarının ürünlerini kiracı kataloğundan yükler ve <see cref="ExaminationTemplate.CheckLineProduct"/> kuralını uygular.
/// Kaydetme ve uygulama aynı yolu kullanır (kural tek yerde).
/// </summary>
internal static class ExaminationTemplateLineResolver
{
    public static async Task<Result<IReadOnlyDictionary<Guid, Product>>> ResolveAsync(
        IReadRepository<Product> products,
        Guid tenantId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0)
            return Result<IReadOnlyDictionary<Guid, Product>>.Success(new Dictionary<Guid, Product>());

        var byId = (await products.ListAsync(new ProductsByIdsSpec(tenantId, ids), ct)).ToDictionary(p => p.Id);

        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var product))
            {
                return Result<IReadOnlyDictionary<Guid, Product>>.Failure(
                    new Error("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.", ProductExtension(id)));
            }

            var check = ExaminationTemplate.CheckLineProduct(product);
            if (!check.IsSuccess)
            {
                return Result<IReadOnlyDictionary<Guid, Product>>.Failure(
                    new Error(check.Error.Code, check.Error.Message, ProductExtension(id)));
            }
        }

        return Result<IReadOnlyDictionary<Guid, Product>>.Success(byId);
    }

    /// <summary>Hatalı satırın ürünü ProblemDetails'te <c>productId</c> olarak döner (arayüz satırı işaretler).</summary>
    public static IReadOnlyDictionary<string, object?> ProductExtension(Guid productId)
        => new Dictionary<string, object?> { ["productId"] = productId };
}
