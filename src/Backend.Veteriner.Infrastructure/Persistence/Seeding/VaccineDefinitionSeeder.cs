using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Sistem (global) aşı kataloğu satırlarını idempotent ekler. <c>TenantId</c> null; <c>Code</c> üzerinden tekrar engellenir.
/// Kaynaklar (vetinity-product research/sistem-kataloglari-kaynak-arastirmasi-2026-11.md §6): HBS ruhsatlı veteriner
/// biyolojik ürünler (hangi hastalığın aşısı Türkiye'de var), Kuduz Yönetmeliği md. 9 (yıllık kuduz), WSAVA 2024
/// (kedi-köpek sınıfı ve aralıklar), ESCCAP (antiparaziter sıklık), Hayvan Hastalıkları ile Mücadele Genelgesi (resmî adlar).
/// Sınıf yalnız kedi, köpek ve tavşan aşılarında verilir; çiftlik ve kanatlı aşıları devlet programına bağlıdır, sınıfı boştur.
/// </summary>
public static class VaccineDefinitionSeeder
{
    private sealed record Entry(
        string Code,
        string Name,
        string? SpeciesCode = null,
        VaccineCategory? Category = null,
        int? DefaultNextDueDays = null);

    private static readonly IReadOnlyList<Entry> GlobalCatalog =
    [
        // Kedi ve köpek
        new("RABIES", "Kuduz", null, VaccineCategory.Core, 365),
        new("MIXED_DOG", "Köpek Karma (DHPPi±L)", "DOG", VaccineCategory.Core, 365),
        new("MIXED_CAT", "Kedi Karma (FVRCP)", "CAT", VaccineCategory.Core, 365),
        new("MIXED_RABIES_DOG", "Köpek Karma + Kuduz", "DOG", VaccineCategory.Core, 365),
        new("MIXED_RABIES_CAT", "Kedi Karma + Kuduz", "CAT", VaccineCategory.Core, 365),
        new("DISTEMPER_PARVO", "Köpek DP (Distemper-Parvovirüs)", "DOG", VaccineCategory.Core, 21),
        new("PARVO", "Köpek Parvovirüs (CPV-2)", "DOG", VaccineCategory.Core, 21),
        new("LEPTOSPIROSIS", "Leptospiroz", null, VaccineCategory.Core, 365),
        new("BRONCHINE", "Bordetella / Parainfluenza (Bronşin)", "DOG", VaccineCategory.NonCore, 365),
        new("LYME", "Lyme (Borelyoz)", "DOG", VaccineCategory.NonCore, 365),
        new("CORONA", "Köpek Koronavirüs Enteriti (CCoV)", "DOG", VaccineCategory.NotRecommended),
        new("LEUKEMIA", "Kedi Lösemisi (FeLV)", "CAT", VaccineCategory.NonCore, 365),
        new("CHLAMYDIA", "Kedi Klamidyozu (Chlamydia felis)", "CAT", VaccineCategory.NonCore, 365),
        new("DERMATOPHYTOSIS", "Mantar (Dermatofitoz)", null, VaccineCategory.NotRecommended),
        // Antiparaziter uygulamalar aşı değildir (ESCCAP); karnede birlikte izlenir.
        new("INTERNAL_PARASITE", "İç Parazit", null, VaccineCategory.ParasiteControl, 90),
        new("EXTERNAL_PARASITE", "Dış Parazit", null, VaccineCategory.ParasiteControl),
        new("INTERNAL_EXTERNAL_PARASITE", "İç-Dış Parazit", null, VaccineCategory.ParasiteControl),
        // Tavşan
        new("RHD", "Tavşanların Viral Hemorajik Hastalığı (RHD)", "RABBIT", VaccineCategory.Core, 365),
        new("MYXOMATOSIS", "Miksomatoz", "RABBIT", VaccineCategory.Core, 365),
        // Kanatlı
        new("NEWCASTLE", "Newcastle Hastalığı"),
        new("PPMV1", "Güvercin Paramiksovirüs (PPMV-1)", "BIRD"),
        new("MAREK", "Marek Hastalığı", "CHICKEN"),
        new("GUMBORO", "Gumboro Hastalığı", "CHICKEN"),
        new("INFECTIOUS_BRONCHITIS", "Enfeksiyöz Bronşit", "CHICKEN"),
        new("FOWL_POX", "Kanatlı Çiçeği", "CHICKEN"),
        new("ILT", "Enfeksiyöz Laringotrakeitis (ILT)", "CHICKEN"),
        // At
        new("TETANUS", "Tetanoz"),
        new("EQUINE_INFLUENZA", "At İnfluenzası", "HORSE"),
        new("EHV", "At Herpesvirüsü (EHV-1/4)", "HORSE"),
        // Çiftlik hayvanları
        new("FOOT_AND_MOUTH", "Şap"),
        new("BRUCELLOSIS_S19", "Sığır Brusellozu (S19)", "CATTLE"),
        new("BRUCELLOSIS_REV1", "Koyun-Keçi Brusellozu (Rev-1)"),
        new("ENTEROTOXEMIA", "Enterotoksemi"),
        new("MASTITIS", "Mastitis"),
        new("SHEEP_GOAT_POX", "Koyun ve Keçi Çiçeği"),
        new("ANTHRAX", "Şarbon (Antraks)"),
        new("BLACKLEG", "Yanıkara"),
        new("LUMPY_SKIN", "Sığırların Nodüler Ekzantemi (LSD)", "CATTLE"),
        new("PPR", "Koyun ve Keçi Vebası (PPR)"),
        new("IBR", "IBR (Enfeksiyöz Bovine Rinotrakeitis)", "CATTLE"),
        new("BVD", "BVD (Bovine Viral Diyare)", "CATTLE"),
        new("CALF_DIARRHEA", "Buzağı İshali (E. coli / Rota / Corona)", "CATTLE"),
        new("PASTEURELLOSIS", "Pastörelloz"),
        new("CLOSTRIDIAL", "Klostridyal Hastalıklar"),
        new("THEILERIOSIS", "Theileriozis", "CATTLE"),
        new("BLUETONGUE", "Mavi Dil"),
        new("AGALACTIA", "Bulaşıcı Agalaksi"),
        new("ORF", "Ektima"),
        new("ENZOOTIC_ABORTION", "Enzootik Abort"),
    ];

    /// <summary>
    /// Katalogdan kaldırılan satırlar (silinmez; geçmiş aşı kayıtları bağlı kalır). Karma ve karma + kuduz türe göre ikiye,
    /// bruselloz S19 ve Rev-1 olarak ayrıldı; FIV ve FIP aşılarının Türkiye'de ruhsatlı ürünü yok, WSAVA FIP'i önermez.
    /// </summary>
    private static readonly IReadOnlyList<string> Deactivations = ["MIXED", "MIXED_RABIES", "BRUCELLOSIS", "FIV", "FIP"];

    /// <summary>
    /// Veteriner literatürüne göre düzeltilen sistem adları: kayıt yalnız eski sistem adını taşıyorsa katalogdaki
    /// yeni ada geçer; yöneticinin elle verdiği ad korunur.
    /// </summary>
    private static readonly IReadOnlyList<(string Code, string OldName)> Renames =
    [
        ("BRONCHINE", "Bronşin"),
        ("BRONCHINE", "Bronşin (Bordetella)"),
        ("LEUKEMIA", "Lösemi"),
        ("LEUKEMIA", "Lösemi (FeLV)"),
        ("CORONA", "Corona"),
        ("CORONA", "Koronavirüs"),
        ("LYME", "Lyme"),
        ("DERMATOPHYTOSIS", "Mantar"),
        ("DISTEMPER_PARVO", "DP (Distemper-Parvo)"),
        ("PARVO", "Parvo"),
        ("RHD", "Tavşan Hemorajik Hastalığı (RHD)"),
        ("NEWCASTLE", "Newcastle"),
        ("EQUINE_INFLUENZA", "At Gribi"),
        ("SHEEP_GOAT_POX", "Koyun-Keçi Çiçeği"),
        ("ANTHRAX", "Şarbon"),
        ("LUMPY_SKIN", "Nodüler Ekzantem (LSD)"),
        ("PPR", "Küçük Ruminant Vebası (PPR)"),
        ("PPR", "Küçük Ruminantların Vebası (PPR)"),
    ];

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var utcNow = TimeProvider.System.GetUtcNow().UtcDateTime;
        var speciesIds = await db.Species
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.Ordinal, ct);

        Guid? SpeciesOf(Entry e) =>
            e.SpeciesCode is { } code && speciesIds.TryGetValue(code, out var id) ? id : null;

        var added = 0;
        foreach (var entry in GlobalCatalog)
        {
            var exists = await db.VaccineDefinitions.AnyAsync(v => v.TenantId == null && v.Code == entry.Code, ct);
            if (exists)
                continue;

            var row = VaccineDefinition.CreateGlobal(
                entry.Code, entry.Name, utcNow, description: null, entry.DefaultNextDueDays, speciesId: SpeciesOf(entry));
            row.SetCategory(entry.Category, utcNow);
            await db.VaccineDefinitions.AddAsync(row, ct);
            added++;
        }

        var catalogCodes = GlobalCatalog.Select(x => x.Code).Concat(Deactivations).ToArray();
        var systemRows = await db.VaccineDefinitions
            .Where(v => v.TenantId == null && catalogCodes.Contains(v.Code))
            .ToListAsync(ct);

        var updated = 0;
        foreach (var row in systemRows)
        {
            var changed = false;

            // Sistem satırı kilidi (kiracı değiştiremez).
            if (!row.IsCore)
            {
                row.SetIsCore(true, utcNow);
                changed = true;
            }

            if (Deactivations.Contains(row.Code))
            {
                if (row.IsActive)
                {
                    row.Deactivate(utcNow);
                    changed = true;
                }

                updated += changed ? 1 : 0;
                continue;
            }

            var entry = GlobalCatalog.Single(x => x.Code == row.Code);

            if (Renames.Any(r => r.Code == row.Code && r.OldName == row.Name))
            {
                row.UpdateDetails(row.Code, entry.Name, row.Description, row.DefaultNextDueDays, row.SpeciesId, row.IsCore, utcNow);
                changed = true;
            }

            // Sınıf, tür ve varsayılan aralık yalnız boşsa doldurulur (sonradan elle verilen değer korunur).
            if (row.Category is null && entry.Category is not null)
            {
                row.SetCategory(entry.Category, utcNow);
                changed = true;
            }

            if ((row.SpeciesId is null && SpeciesOf(entry) is not null) || (row.DefaultNextDueDays is null && entry.DefaultNextDueDays is not null))
            {
                row.UpdateDetails(
                    row.Code,
                    row.Name,
                    row.Description,
                    row.DefaultNextDueDays ?? entry.DefaultNextDueDays,
                    row.SpeciesId ?? SpeciesOf(entry),
                    row.IsCore,
                    utcNow);
                changed = true;
            }

            updated += changed ? 1 : 0;
        }

        if (added > 0 || updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation(
                "VaccineDefinitionSeeder: {Added} global vaccine definition(s) added, {Updated} updated.",
                added,
                updated);
        }
    }
}
