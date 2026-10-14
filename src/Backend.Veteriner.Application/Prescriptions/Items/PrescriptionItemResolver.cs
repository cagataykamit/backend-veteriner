using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Prescriptions.Items;

/// <summary>
/// Reçete satırı isteklerini domain girdisine çevirir: katalog ürünü kiracıda var ve aktif olmalı,
/// ürün verilip ad boşsa ürün adı kopyalanır. Stok okunmaz/yazılmaz (reçete stok düşmez).
/// </summary>
internal static class PrescriptionItemResolver
{
    public static async Task<Result<IReadOnlyList<PrescriptionItemDraft>>> ResolveAsync(
        IReadRepository<Product> products,
        Guid tenantId,
        IReadOnlyList<PrescriptionItemRequest> items,
        CancellationToken ct)
    {
        var drafts = new List<PrescriptionItemDraft>(items.Count);
        var productsById = new Dictionary<Guid, Product>();

        foreach (var item in items)
        {
            var name = item.Name;
            if (item.ProductId is { } productId && productId != Guid.Empty)
            {
                if (!productsById.TryGetValue(productId, out var product))
                {
                    product = await products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, productId), ct);
                    if (product is null)
                    {
                        return Result<IReadOnlyList<PrescriptionItemDraft>>.Failure(
                            "Products.NotFound",
                            "Ürün bulunamadı veya kiracıya ait değil.");
                    }

                    productsById[productId] = product;
                }

                if (!product.IsActive)
                {
                    return Result<IReadOnlyList<PrescriptionItemDraft>>.Failure(
                        "Products.Inactive",
                        "Pasif ürün reçete satırına eklenemez.");
                }

                if (!product.IsStockTracked)
                {
                    return Result<IReadOnlyList<PrescriptionItemDraft>>.Failure(
                        "Products.NotStockTracked",
                        "Hizmet kalemi reçete satırına eklenemez.");
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = product.Name;
            }

            drafts.Add(new PrescriptionItemDraft(name ?? string.Empty, item.ProductId, item.Instructions, item.Quantity));
        }

        return Result<IReadOnlyList<PrescriptionItemDraft>>.Success(drafts);
    }
}
