using Backend.Veteriner.Application.Imports.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Kayıtlı ürün adlarının noktasız ı onarımı (DbMigrator <c>fix-product-names</c>): aktarmadaki kural
/// (<see cref="ProductNameSpelling.Correct"/>) tek kiracının mevcut ürünlerine uygulanır. Tekrar çalıştırılabilir: düzelmiş ad
/// değişmez. Yalnız <c>Products.Name</c> değişir; satış ve ücret satırlarındaki ad kopyaları (satış anındaki ad) olduğu gibi kalır.
/// </summary>
public static class ProductNameRepair
{
    public sealed record Change(Guid ProductId, string Before, string After);

    /// <returns>Değişen adlar; <paramref name="dryRun"/> ise değişecek adlar (veritabanına yazılmaz).</returns>
    public static async Task<IReadOnlyList<Change>> RunAsync(
        AppDbContext db, Guid tenantId, bool dryRun, TimeProvider timeProvider, ILogger logger, CancellationToken ct = default)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            throw new InvalidOperationException($"Kiracı bulunamadı: {tenantId}");

        // Ad filtresi bellekte: veritabanı harmanlaması ı ile i'yi eşit sayabilir.
        var products = await db.Products.Where(p => p.TenantId == tenantId).OrderBy(p => p.Name).ToListAsync(ct);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var changes = new List<Change>();

        foreach (var product in products)
        {
            var corrected = ProductNameSpelling.Correct(product.Name);
            if (string.Equals(corrected, product.Name, StringComparison.Ordinal))
                continue;

            changes.Add(new Change(product.Id, product.Name, corrected));
            logger.LogInformation("Product name {Mode}: {ProductId} \"{Before}\" -> \"{After}\"",
                dryRun ? "dry-run" : "fixed", product.Id, product.Name, corrected);
            if (!dryRun)
                product.Rename(corrected, utcNow);
        }

        if (!dryRun && changes.Count > 0)
            await db.SaveChangesAsync(ct);

        return changes;
    }
}
