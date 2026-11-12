using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Sistem (global) tanı/şikâyet sözlüğünü idempotent ekler (ADR-020 S3). <c>TenantId</c> null; tür + Türkçe anahtar
/// (<see cref="TurkishTextKey"/>) üzerinden tekrar engellenir. Sistem satırları yalnız <see cref="Revisions"/> ile
/// yeniden adlandırılır ya da pasifleşir; silinmez.
/// Kaynak (vetinity-product research/sistem-kataloglari-kaynak-arastirmasi-2026-11.md §7): birinci basamak sıklık
/// çalışmaları (VetCompass, O'Neill ve ark. 2014 köpek, 2023 kedi, 2020 tavşan), WOAH listelenmiş hastalıklar ve Türkiye
/// ihbarı mecburi hastalıklar listesi, standart ders kitabı terimleri. VeNom/SNOMED listesi kopyalanmaz (lisans).
/// </summary>
public static class ClinicalTermSeeder
{
    public static readonly IReadOnlyList<string> Complaints =
    [
        "İştahsızlık",
        "Kusma",
        "Öğürme",
        "Yiyeceği geri çıkarma (regürjitasyon)",
        "İshal",
        "Kanlı dışkı",
        "Kabızlık",
        "Dışkı azalması / yokluğu",
        "Halsizlik",
        "Kilo kaybı",
        "Kilo alma",
        "Aşırı iştah",
        "Ateş",
        "Öksürük",
        "Hapşırma",
        "Burun akıntısı",
        "Burun kanaması",
        "Nefes darlığı",
        "Hırıltılı solunum",
        "Göz akıntısı",
        "Gözde kızarıklık",
        "Gözü kısma / göz kaşıma",
        "Görme kaybı",
        "Kulak kaşıma / baş sallama",
        "Kaşıntı (deri)",
        "Aşırı yalama / tımarlanma",
        "Tüy dökülmesi",
        "Deride kızarıklık",
        "Yara",
        "Tırnak uzaması / kırılması",
        "Poposunu yere sürtme",
        "Topallık",
        "Felç / yürüyememe",
        "Şişlik / kitle",
        "Karın şişliği",
        "Aşırı su içme",
        "Sık idrara çıkma",
        "İdrar yapamama / ıkınma",
        "İdrar kaçırma",
        "Kanlı idrar",
        "Vajinal akıntı",
        "Doğum güçlüğü",
        "Yumurtlama sorunu",
        "Perine kirlenmesi",
        "Ağız kokusu",
        "Salya artışı",
        "Yutkunma güçlüğü",
        "Sarılık",
        "Solgunluk",
        "Nöbet",
        "Titreme",
        "Bayılma / çökme",
        "Dengesizlik / koordinasyon bozukluğu",
        "Davranış değişikliği",
        "Saldırganlık",
        "Ağrı belirtisi",
        "Tüy yolma",
        "Kanama",
        "Travma / kaza",
    ];

    public static readonly IReadOnlyList<string> Diagnoses =
    [
        // Sindirim
        "Gastroenterit",
        "Parvoviral enterit",
        "Pankreatit",
        "Yabancı cisim (gastrointestinal)",
        "Gastrointestinal staz / ileus",
        "Megaözofagus",
        "Hepatopati",
        "Anal kese tıkanıklığı / hastalığı",
        "İç parazit enfestasyonu",
        "Giardiyoz",
        // Deri, kulak, göz
        "Dış parazit enfestasyonu",
        "Pire enfestasyonu",
        "Otitis externa",
        "Otitis media",
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
        "Tüy örtüsü bozukluğu",
        "Psikojenik alopesi",
        "Miyazis",
        "Abse",
        "Isırık yarası",
        "Laserasyon / yara",
        "Yanık",
        "Postoperatif yara komplikasyonu",
        "Tırnak uzaması",
        "Tırnak yaralanması",
        // Ağız ve diş
        "Periodontal hastalık / diş taşı",
        "Diş hastalığı (periodontal dışı)",
        "Diş kırığı",
        "Kedi kronik gingivostomatiti",
        "Azı dişi uzaması",
        "Kesici diş uzaması",
        // Solunum
        "Üst solunum yolu enfeksiyonu",
        "Kedi üst solunum yolu enfeksiyonu (FHV-1 / FCV)",
        "Bronşit",
        "Pnömoni",
        // Üriner ve üreme
        "Sistit",
        "Kedi alt üriner sistem hastalığı (FLUTD)",
        "Ürolitiyazis",
        "Üretral obstrüksiyon",
        "Kronik böbrek hastalığı",
        "Piyometra",
        "Mastitis",
        "Distosi",
        "Eklampsi (puerperal hipokalsemi)",
        "Kriptorşidizm",
        // Endokrin ve metabolik
        "Diabetes mellitus",
        "Hipotiroidizm",
        "Hipertiroidizm",
        "Hiperadrenokortisizm (Cushing)",
        "Hipoadrenokortisizm (Addison)",
        "Obezite",
        "Metabolik kemik hastalığı (sürüngen)",
        // Kalp ve kan
        "Kalp üfürümü",
        "Miksomatöz mitral kapak hastalığı (MMVD)",
        "Hipertrofik kardiyomiyopati",
        "Kardiyak aritmi",
        "Kalp yetmezliği",
        "Anemi",
        // Kas-iskelet ve sinir
        "Osteoartrit",
        "Kırık",
        "Ön çapraz bağ kopması",
        "Patella luksasyonu",
        "Kalça displazisi",
        "İntervertebral disk hastalığı",
        "Epilepsi",
        // Enfeksiyöz
        "Gençlik hastalığı (Distemper)",
        "Panlökopeni (kedi parvovirüs enfeksiyonu)",
        "Kedi enfeksiyöz peritoniti (FIP)",
        "FeLV enfeksiyonu",
        "FIV enfeksiyonu",
        "Leptospiroz",
        "Leishmaniyoz",
        "Babesiyoz",
        "Erlihiyoz",
        "Ekinokokkoz / kist hidatik",
        "Psittakoz (kuş klamidyozu)",
        "Miksomatozis",
        "Tavşan hemorajik hastalığı",
        // İhbarı mecburi (Türkiye) ve WOAH listesi
        "Kuduz şüphesi",
        "Kuş gribi şüphesi",
        "Newcastle hastalığı",
        "Kedilerin süngerimsi beyin hastalığı (FSE) şüphesi",
        // Tümör
        "Lipom",
        "Deri kitlesi",
        "Lenfoma",
        "Meme tümörü",
        "Mast hücre tümörü",
        "Kitle / tümör (tanımlanmamış)",
        // Diğer
        "Zehirlenme",
        "Isı çarpması",
        "Davranış bozukluğu",
        "Kuşta tüy yolma",
    ];

    /// <summary>
    /// Sistem satırı düzeltmeleri (2026-11-13): eski adlı satır yeni ada geçer; yeni ad <c>null</c> ise satır pasifleşir.
    /// "Genel kontrol" şikâyet değil ziyaret nedenidir (VeNom'da ayrı liste; şablonlarda ziyaret nedeni olarak kalır).
    /// </summary>
    private static readonly IReadOnlyList<(ClinicalTermKind Kind, string OldName, string? NewName)> Revisions =
    [
        (ClinicalTermKind.Complaint, "Genel kontrol", null),
        (ClinicalTermKind.Complaint, "Kaşıntı", "Kaşıntı (deri)"),
        (ClinicalTermKind.Complaint, "Deride kızarıklık / yara", "Deride kızarıklık"),
        (ClinicalTermKind.Complaint, "İdrar yapamama", "İdrar yapamama / ıkınma"),
        (ClinicalTermKind.Complaint, "Dengesizlik", "Dengesizlik / koordinasyon bozukluğu"),
        (ClinicalTermKind.Diagnosis, "Distemper", "Gençlik hastalığı (Distemper)"),
        (ClinicalTermKind.Diagnosis, "Panlökopeni", "Panlökopeni (kedi parvovirüs enfeksiyonu)"),
    ];

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var utcNow = TimeProvider.System.GetUtcNow().UtcDateTime;

        // Düzeltmeler eklemeden önce: yeniden adlandırılan satır yeni adla ikinci kez eklenmesin.
        var revised = 0;
        foreach (var (kind, oldName, newName) in Revisions)
        {
            var oldKey = TurkishTextKey.Normalize(oldName);
            var row = await db.ClinicalTerms.FirstOrDefaultAsync(
                t => t.TenantId == null && t.Kind == kind && t.NormalizedName == oldKey, ct);
            if (row is null)
                continue;

            if (newName is null)
            {
                if (!row.IsActive)
                    continue;
                row.ReviseSystemTerm(row.Name, isActive: false, utcNow);
            }
            else
            {
                var newKey = TurkishTextKey.Normalize(newName);
                if (await db.ClinicalTerms.AnyAsync(t => t.TenantId == null && t.Kind == kind && t.NormalizedName == newKey, ct))
                    continue;
                row.ReviseSystemTerm(newName, row.IsActive, utcNow);
            }

            revised++;
        }

        if (revised > 0)
            await db.SaveChangesAsync(ct);

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

            await db.ClinicalTerms.AddAsync(ClinicalTerm.CreateSystem(kind, name, utcNow), ct);
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(ct);

        if (added > 0 || revised > 0)
        {
            logger?.LogInformation(
                "ClinicalTermSeeder: {Added} system clinical term(s) added, {Revised} revised.",
                added,
                revised);
        }
    }
}
