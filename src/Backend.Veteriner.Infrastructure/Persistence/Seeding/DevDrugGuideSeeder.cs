using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// YALNIZCA geliştirme ortamı: ilaç rehberi ekranını elle denemek için birkaç elle yazılmış ÖRNEK ruhsatlı ürün
/// (REF-001). Bakanlık verisinin toplu alımı izne bağlıdır (ADR-049 açık karar 2); bu kayıtlar HBS'den alınmadı, ürün
/// adları ve ruhsat sahipleri uydurmadır ("ÖRNEK" önekli). Etken madde adları genel adlardır. Idempotent: aynı adlı
/// örnek ürün varsa eklenmez. Normal <c>seed</c> hattına dahil değildir; <c>seed-dev-drug-guide</c> komutuyla çalışır.
/// </summary>
public static class DevDrugGuideSeeder
{
    public const string Source = "ÖRNEK";

    private sealed record Sample(string Name, string Holder, string? LicenseNumber, string[] Ingredients, (string Label, Guid? SpeciesId)[] Species);

    private static readonly Sample[] Samples =
    [
        new("ÖRNEK Amoksisilin + Klavulanik Asit 250 mg Tablet", "Örnek Veteriner İlaç A.Ş.", "ÖRNEK-0001",
            ["Amoksisilin", "Klavulanik asit"], [("Kedi", SpeciesSeedConstants.Cat), ("Köpek", SpeciesSeedConstants.Dog)]),
        new("ÖRNEK Meloksikam 1,5 mg/ml Oral Süspansiyon", "Örnek Veteriner İlaç A.Ş.", "ÖRNEK-0002",
            ["Meloksikam"], [("Köpek", SpeciesSeedConstants.Dog)]),
        new("ÖRNEK Meloksikam 0,5 mg/ml Kedi Süspansiyonu", "Deneme Hayvan Sağlığı Ltd.", null,
            ["Meloksikam"], [("Kedi", SpeciesSeedConstants.Cat)]),
        new("ÖRNEK Prazikuantel + Pirantel Tablet", "Deneme Hayvan Sağlığı Ltd.", "ÖRNEK-0004",
            ["Prazikuantel", "Pirantel"], [("Köpek", SpeciesSeedConstants.Dog)]),
        new("ÖRNEK İvermektin %1 Enjeksiyonluk Çözelti", "Deneme Hayvan Sağlığı Ltd.", "ÖRNEK-0005",
            ["İvermektin"], [("Sığır", SpeciesSeedConstants.Cattle), ("Koyun", SpeciesSeedConstants.Sheep), ("Keçi", SpeciesSeedConstants.Goat)]),
    ];

    /// <returns>Eklenen ürün sayısı.</returns>
    public static async Task<int> SeedAsync(AppDbContext db, IHostEnvironment environment, TimeProvider timeProvider, ILogger? logger = null, CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("İlaç rehberi örnek seed'i yalnızca Development ortamında çalışır.");

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var existingNames = await db.Set<LicensedProduct>().Select(p => p.Name).ToListAsync(ct);
        var ingredients = await db.Set<ActiveIngredient>().ToDictionaryAsync(i => i.NormalizedName, ct);
        var added = 0;

        foreach (var sample in Samples.Where(s => !existingNames.Contains(s.Name)))
        {
            var ingredientIds = sample.Ingredients.Select(name =>
            {
                var key = TurkishTextKey.Normalize(name);
                if (!ingredients.TryGetValue(key, out var ingredient))
                {
                    ingredient = new ActiveIngredient(name);
                    ingredients[key] = ingredient;
                    db.Add(ingredient);
                }

                return ingredient.Id;
            }).ToList();

            db.Add(new LicensedProduct(
                sample.Name,
                sample.Holder,
                sample.LicenseNumber,
                spcUrl: null,
                Source,
                ingredientIds,
                sample.Species.Select(s => new LicensedProductTargetSpeciesDraft(s.Label, s.SpeciesId)).ToList(),
                utcNow));
            added++;
        }

        await db.SaveChangesAsync(ct);
        logger?.LogInformation("Dev drug guide seed: {Added} örnek ruhsatlı ürün eklendi.", added);
        return added;
    }
}
