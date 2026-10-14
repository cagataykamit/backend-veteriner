using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Sistem (global) tanı/şikâyet sözlüğünü idempotent ekler (ADR-020 S3). <c>TenantId</c> null; tür + Türkçe anahtar
/// (<see cref="TurkishTextKey"/>) üzerinden tekrar engellenir. Mevcut satırları değiştirmez/silmez.
/// İçerik: veteriner pratiğinde yaygın genel terimler (Türkçe); pilotta kliniklerle doğrulanır.
/// </summary>
public static class ClinicalTermSeeder
{
    public static readonly IReadOnlyList<string> Complaints =
    [
        "Genel kontrol",
        "İştahsızlık",
        "Kusma",
        "İshal",
        "Kabızlık",
        "Halsizlik",
        "Kilo kaybı",
        "Ateş",
        "Öksürük",
        "Hapşırma",
        "Burun akıntısı",
        "Nefes darlığı",
        "Göz akıntısı",
        "Gözde kızarıklık",
        "Kulak kaşıma / baş sallama",
        "Kaşıntı",
        "Tüy dökülmesi",
        "Deride kızarıklık / yara",
        "Topallık",
        "Şişlik / kitle",
        "Aşırı su içme",
        "Sık idrara çıkma",
        "İdrar yapamama",
        "Kanlı idrar",
        "Ağız kokusu",
        "Salya artışı",
        "Nöbet",
        "Dengesizlik",
        "Kanama",
        "Travma / kaza",
    ];

    public static readonly IReadOnlyList<string> Diagnoses =
    [
        "Gastroenterit",
        "Parvoviral enterit",
        "Pankreatit",
        "Yabancı cisim (gastrointestinal)",
        "İç parazit enfestasyonu",
        "Dış parazit enfestasyonu",
        "Otitis externa",
        "Kulak uyuzu (otodektik)",
        "Kulak hematomu",
        "Konjonktivit",
        "Keratit / kornea ülseri",
        "Atopik dermatit",
        "Pire alerjisi dermatiti",
        "Piyoderma",
        "Dermatofitoz",
        "Sarkoptik uyuz",
        "Demodikoz",
        "Abse",
        "Periodontal hastalık / diş taşı",
        "Üst solunum yolu enfeksiyonu",
        "Bronşit",
        "Pnömoni",
        "Sistit",
        "Kedi alt üriner sistem hastalığı (FLUTD)",
        "Üretral obstrüksiyon",
        "Kronik böbrek hastalığı",
        "Diabetes mellitus",
        "Hipotiroidizm",
        "Hipertiroidizm",
        "Kalp yetmezliği",
        "Anemi",
        "Obezite",
        "Osteoartrit",
        "Kırık",
        "Ön çapraz bağ kopması",
        "Patella luksasyonu",
        "Epilepsi",
        "Zehirlenme",
        "Piyometra",
        "Mastitis",
        "Kitle / tümör (tanımlanmamış)",
        "Distemper",
        "Panlökopeni",
    ];

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var existing = await db.ClinicalTerms
            .Where(t => t.TenantId == null)
            .Select(t => new { t.Kind, t.NormalizedName })
            .ToListAsync(ct);
        var known = existing.Select(t => (t.Kind, t.NormalizedName)).ToHashSet();

        var added = 0;
        foreach (var (kind, name) in Complaints.Select(n => (ClinicalTermKind.Complaint, n))
                     .Concat(Diagnoses.Select(n => (ClinicalTermKind.Diagnosis, n))))
        {
            if (!known.Add((kind, TurkishTextKey.Normalize(name))))
                continue;

            await db.ClinicalTerms.AddAsync(ClinicalTerm.CreateSystem(kind, name), ct);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("ClinicalTermSeeder: {Count} system clinical term(s) added.", added);
        }
    }
}
