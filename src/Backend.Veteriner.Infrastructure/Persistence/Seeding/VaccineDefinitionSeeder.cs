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
        ("BRONCHINE", "Bronşin"),
        ("LEUKEMIA", "Lösemi"),
        ("FIV", "FIV"),
        ("FIP", "FIP"),
        ("INTERNAL_PARASITE", "İç Parazit"),
        ("EXTERNAL_PARASITE", "Dış Parazit"),
        ("CORONA", "Corona"),
        ("LYME", "Lyme"),
    ];

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var normalizedCodes = GlobalCatalog
            .Select(x => x.Code.Trim().ToUpperInvariant())
            .Distinct()
            .ToArray();

        var added = 0;
        foreach (var (code, name) in GlobalCatalog)
        {
            var normalized = code.Trim().ToUpperInvariant();
            var exists = await db.VaccineDefinitions.AnyAsync(
                v => v.TenantId == null && v.Code == normalized,
                ct);
            if (exists)
                continue;

            await db.VaccineDefinitions.AddAsync(
                VaccineDefinition.CreateGlobal(code, name, description: null, defaultNextDueDays: null, speciesId: null),
                ct);
            added++;
        }

        var coreRepair = await db.VaccineDefinitions
            .Where(v => v.TenantId == null && normalizedCodes.Contains(v.Code) && !v.IsCore)
            .ToListAsync(ct);

        foreach (var row in coreRepair)
            row.SetIsCore(true);

        if (added > 0 || coreRepair.Count > 0)
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
