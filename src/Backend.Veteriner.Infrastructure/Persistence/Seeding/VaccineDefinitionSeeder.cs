using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Sistem (global) aşı kataloğu satırlarını idempotent ekler. <c>TenantId</c> null; <c>Code</c> üzerinden tekrar engellenir.
/// </summary>
public static class VaccineDefinitionSeeder
{
    private static readonly IReadOnlyList<(string Code, string Name)> GlobalCatalog =
    [
        ("RABIES", "Kuduz"),
        ("MIXED", "Karma"),
        ("BRONCHINE", "Bronşin (Bordetella)"),
        ("LEUKEMIA", "Lösemi (FeLV)"),
        ("FIV", "FIV"),
        ("FIP", "FIP"),
        ("INTERNAL_PARASITE", "İç Parazit"),
        ("EXTERNAL_PARASITE", "Dış Parazit"),
        ("CORONA", "Koronavirüs"),
        ("LYME", "Lyme (Borelyoz)"),
        // Kedi/köpek: kombine ve Türkiye'de yaygın uygulamalar (E-vet geçişi, 2026-11-12).
        ("INTERNAL_EXTERNAL_PARASITE", "İç-Dış Parazit"),
        ("MIXED_RABIES", "Karma + Kuduz"),
        ("DERMATOPHYTOSIS", "Mantar (Dermatofitoz)"),
        ("DISTEMPER_PARVO", "DP (Distemper-Parvo)"),
        ("PARVO", "Parvo"),
        ("LEPTOSPIROSIS", "Leptospiroz"),
        // Tavşan, kuş
        ("RHD", "Tavşanların Viral Hemorajik Hastalığı (RHD)"),
        ("MYXOMATOSIS", "Miksomatoz"),
        ("NEWCASTLE", "Newcastle"),
        // At
        ("TETANUS", "Tetanoz"),
        ("EQUINE_INFLUENZA", "At İnfluenzası"),
        // Çiftlik hayvanları
        ("FOOT_AND_MOUTH", "Şap"),
        ("BRUCELLOSIS", "Brusella"),
        ("ENTEROTOXEMIA", "Enterotoksemi"),
        ("MASTITIS", "Mastitis"),
        ("SHEEP_GOAT_POX", "Koyun ve Keçi Çiçeği"),
        ("ANTHRAX", "Şarbon"),
        ("BLACKLEG", "Yanıkara"),
        ("LUMPY_SKIN", "Sığırların Nodüler Ekzantemi (LSD)"),
        ("PPR", "Koyun ve Keçi Vebası (PPR)"),
    ];

    /// <summary>
    /// Veteriner literatürüne göre düzeltilen sistem adları (2026-11-12): kayıt yalnız eski sistem adını taşıyorsa katalogdaki
    /// yeni ada geçer; yöneticinin elle verdiği ad korunur.
    /// </summary>
    private static readonly IReadOnlyList<(string Code, string OldName)> Renames =
    [
        ("BRONCHINE", "Bronşin"),
        ("LEUKEMIA", "Lösemi"),
        ("CORONA", "Corona"),
        ("LYME", "Lyme"),
        ("DERMATOPHYTOSIS", "Mantar"),
        ("RHD", "Tavşan Hemorajik Hastalığı (RHD)"),
        ("EQUINE_INFLUENZA", "At Gribi"),
        ("SHEEP_GOAT_POX", "Koyun-Keçi Çiçeği"),
        ("LUMPY_SKIN", "Nodüler Ekzantem (LSD)"),
        ("PPR", "Küçük Ruminant Vebası (PPR)"),
        ("PPR", "Küçük Ruminantların Vebası (PPR)"),
    ];

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var normalizedCodes = GlobalCatalog
            .Select(x => x.Code.Trim().ToUpperInvariant())
            .Distinct()
            .ToArray();

        var added = 0;
        var utcNow = TimeProvider.System.GetUtcNow().UtcDateTime;
        foreach (var (code, name) in GlobalCatalog)
        {
            var normalized = code.Trim().ToUpperInvariant();
            var exists = await db.VaccineDefinitions.AnyAsync(
                v => v.TenantId == null && v.Code == normalized,
                ct);
            if (exists)
                continue;

            await db.VaccineDefinitions.AddAsync(
                VaccineDefinition.CreateGlobal(code, name, utcNow, description: null, defaultNextDueDays: null, speciesId: null),
                ct);
            added++;
        }

        var coreRepair = await db.VaccineDefinitions
            .Where(v => v.TenantId == null && normalizedCodes.Contains(v.Code) && !v.IsCore)
            .ToListAsync(ct);

        foreach (var row in coreRepair)
            row.SetIsCore(true, utcNow);

        var renamed = 0;
        foreach (var (code, oldName) in Renames)
        {
            var row = await db.VaccineDefinitions.FirstOrDefaultAsync(
                v => v.TenantId == null && v.Code == code && v.Name == oldName,
                ct);
            if (row is null)
                continue;

            var newName = GlobalCatalog.Single(x => x.Code == code).Name;
            row.UpdateDetails(row.Code, newName, row.Description, row.DefaultNextDueDays, row.SpeciesId, row.IsCore, utcNow);
            renamed++;
        }

        if (renamed > 0)
            logger?.LogInformation("VaccineDefinitionSeeder: {Count} global vaccine definition(s) renamed.", renamed);

        if (added > 0 || coreRepair.Count > 0 || renamed > 0)
        {
            await db.SaveChangesAsync(ct);
            if (added > 0)
                logger?.LogInformation("VaccineDefinitionSeeder: {Count} global vaccine definition(s) added.", added);
            if (coreRepair.Count > 0)
                logger?.LogInformation(
                    "VaccineDefinitionSeeder: {Count} global row(s) updated to IsCore=true.",
                    coreRepair.Count);
        }
    }
}
