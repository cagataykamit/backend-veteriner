using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Sistem (global) muayene şablonlarını idempotent ekler (ADR-020 S4). <c>TenantId</c> null; Türkçe ad anahtarı
/// (<see cref="TurkishTextKey"/>) üzerinden tekrar engellenir. Önceki sürümün metni hâlâ duran sistem şablonu
/// <see cref="Revisions"/> ile güncellenir; silinmez.
/// İçerik yalnız metindir, olgu bildirmeyen boş başlıklar (hekim doldurur; "yok/normal" gibi hazır sonuç yazılmaz);
/// kiracıdan bağımsız ürün bağı uydurulmaz. Ölçek adları etiket olarak geçer, ölçek metni gömülmez (WSAVA VKS, ağrı
/// ölçekleri telifli). Kaynak: AAHA/AAFP yaşam evresi, AAHA 2020 anestezi, AAHA 2023 yaşlı hayvan kılavuzları, WSAVA 2024
/// ("aşı muayenesi" yerine sağlık kontrolü), VetCompass sıklıkları (vetinity-product
/// research/sistem-kataloglari-kaynak-arastirmasi-2026-11.md §8). Vücut ısısı, nabız, solunum ve kilo muayenenin vital
/// alanlarıdır; şablonda tekrarlanmaz.
/// </summary>
public static class ExaminationTemplateSeeder
{
    public sealed record SystemTemplate(string Name, string? VisitReason, string? Findings, string? Plan);

    /// <summary>Her şablonun bulgularına eklenen ortak değerlendirme başlıkları.</summary>
    private const string CommonAssessment = "VKS (1–9): \nKas kondisyonu: \nHidrasyon: \nAğrı skoru / kullanılan ölçek: ";

    public static readonly IReadOnlyList<SystemTemplate> Templates =
    [
        new(
            "Rutin muayene",
            "Genel kontrol",
            "Genel durum: \nMukozalar / KDZ: \nLenf yumruları: \nKalp-akciğer oskültasyonu: \nKarın palpasyonu: \nDeri / tüy: \nGöz / kulak / ağız: \n"
            + CommonAssessment,
            null),
        new(
            "Sağlık kontrolü ve aşı",
            "Aşı",
            "Genel durum: \nİştah / genel durum: \nAşıya engel bulgu: \n" + CommonAssessment,
            "Uygulanan aşı / parazit uygulaması: \nAşı sonrası gözlem: \nSonraki doz tarihi: "),
        new(
            "Kontrol muayenesi",
            "Kontrol",
            "Önceki şikâyetin seyri: \nTedaviye yanıt: \nGenel durum: \n" + CommonAssessment,
            "Tedaviye devam / sonlandırma kararı: \nSonraki kontrol: "),
        new(
            "Yavru ilk muayenesi",
            "Yavru ilk kontrol",
            "Genel durum: \nDoğumsal bulgular (üfürüm, fıtık, damak, testis inişi): \nDiş ve çene: \nParazit bulgusu: \nBeslenme: \n"
            + CommonAssessment,
            "Aşı ve parazit takvimi: \nBeslenme önerisi: \nSosyalleşme önerisi: \nSonraki kontrol: "),
        new(
            "Yaşlı hayvan muayenesi",
            "Yaşlı hayvan kontrolü",
            "Genel durum: \nDeri, tırnak ve tırnak yatağı: \nLenf yumruları: \nTiroid (kedi): \nKalp-akciğer oskültasyonu: \nKarın palpasyonu: \n"
            + "Eklem ve hareket: \nDavranış / bilişsel değişiklik: \n" + CommonAssessment,
            "Laboratuvar (kan, idrar, tansiyon): \nKronik ağrı yönetimi: \nSonraki kontrol (6 ay): "),
        new(
            "Anestezi öncesi değerlendirme",
            "Operasyon öncesi",
            "Genel durum: \nKalp-akciğer oskültasyonu: \nMukozalar / KDZ: \nAçlık süresi: \nKullandığı ilaçlar: \nLaboratuvar: \nASA sınıfı (1–5, E): \n"
            + CommonAssessment,
            "Anestezi protokolü: \nIV kateter: \nRisk notu ve sahip onayı: "),
        new(
            "Kulak muayenesi",
            "Kulak şikâyeti",
            "Kulak kepçesi: \nDış kulak yolu (otoskopi): \nKulak zarı: \nAkıntı / koku: \nSitoloji: \n" + CommonAssessment,
            "Temizlik / tedavi: \nSonraki kontrol: "),
        new(
            "Ağız ve diş muayenesi",
            "Ağız / diş şikâyeti",
            "Diş taşı: \nDiş eti: \nEksik / kırık diş: \nAğız mukozası: \nAğız kokusu: \n" + CommonAssessment,
            "Diş temizliği / çekim planı: \nEvde bakım önerisi: \nSonraki kontrol: "),
        new(
            "Dermatoloji muayenesi",
            "Deri şikâyeti",
            "Lezyon dağılımı: \nLezyon tipi: \nKaşıntı derecesi: \nDış parazit bulgusu: \nDeri kazıntısı: \nSitoloji / trikogram: \n" + CommonAssessment,
            "Tedavi: \nParazit kontrolü: \nSonraki kontrol: "),
        new(
            "Göz muayenesi",
            "Göz şikâyeti",
            "Göz kapakları: \nKonjonktiva: \nKornea: \nSchirmer testi: \nFloresein testi: \nGöz içi basıncı: \nPupilla refleksleri: \n" + CommonAssessment,
            "Tedavi: \nSonraki kontrol: "),
        new(
            "Ameliyat sonrası kontrol",
            "Ameliyat sonrası",
            "Yara yeri: \nDikişler: \nİştah: \nDışkılama / idrar: \n" + CommonAssessment,
            "Dikiş alma tarihi: \nİlaç devamı: \nSonraki kontrol: "),
        new(
            "Tavşan ve egzotik muayene",
            "Genel kontrol",
            "Genel durum: \nAzı ve kesici dişler: \nTırnaklar: \nPerine: \nDışkı üretimi: \nBeslenme ve ortam: \n" + CommonAssessment,
            "Beslenme / ortam önerisi: \nSonraki kontrol: "),
    ];

    /// <summary>
    /// Önceki sürümün sistem şablonları (2026-11-13'e dek). Metni hâlâ bu sürümle aynıysa yeni sürüme geçer; "Aşı muayenesi"
    /// adı "Sağlık kontrolü ve aşı" olur.
    /// </summary>
    private static readonly IReadOnlyList<(SystemTemplate Old, string NewName)> Revisions =
    [
        (new(
            "Rutin muayene",
            "Genel kontrol",
            "Genel durum: \nMukozalar / KDZ: \nLenf yumruları: \nKalp-akciğer oskültasyonu: \nKarın palpasyonu: \nDeri / tüy: \nGöz / kulak / ağız: ",
            null), "Rutin muayene"),
        (new(
            "Aşı muayenesi",
            "Aşı",
            "Genel durum: \nVücut ısısı ve iştah: \nAşıya engel bulgu: ",
            "Aşı sonrası gözlem: \nSonraki doz tarihi: "), "Sağlık kontrolü ve aşı"),
        (new(
            "Kontrol muayenesi",
            "Kontrol",
            "Önceki şikâyetin seyri: \nTedaviye yanıt: \nGenel durum: ",
            "Tedaviye devam / sonlandırma kararı: \nSonraki kontrol: "), "Kontrol muayenesi"),
    ];

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var utcNow = TimeProvider.System.GetUtcNow().UtcDateTime;

        var revised = 0;
        foreach (var (old, newName) in Revisions)
        {
            var oldKey = TurkishTextKey.Normalize(old.Name);
            var row = await db.ExaminationTemplates.FirstOrDefaultAsync(t => t.TenantId == null && t.NormalizedName == oldKey, ct);
            if (row is null || !Same(row.VisitReason, old.VisitReason) || !Same(row.Findings, old.Findings) || !Same(row.Plan, old.Plan))
                continue;

            var target = Templates.Single(t => t.Name == newName);
            var newKey = TurkishTextKey.Normalize(target.Name);
            if (newKey != oldKey && await db.ExaminationTemplates.AnyAsync(t => t.TenantId == null && t.NormalizedName == newKey, ct))
                continue;

            row.ReviseSystemTemplate(target.Name, target.VisitReason, target.Findings, target.Plan, row.IsActive, utcNow);
            revised++;
        }

        if (revised > 0)
            await db.SaveChangesAsync(ct);

        var known = (await db.ExaminationTemplates
                .Where(t => t.TenantId == null)
                .Select(t => t.NormalizedName)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var template in Templates)
        {
            if (!known.Add(TurkishTextKey.Normalize(template.Name)))
                continue;

            await db.ExaminationTemplates.AddAsync(
                ExaminationTemplate.CreateSystem(template.Name, template.VisitReason, template.Findings, template.Plan, utcNow), ct);
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(ct);

        if (added > 0 || revised > 0)
        {
            logger?.LogInformation(
                "ExaminationTemplateSeeder: {Added} system examination template(s) added, {Revised} revised.",
                added,
                revised);
        }
    }

    /// <summary>Şablon metni saklanırken kırpılır (<see cref="ExaminationTemplate"/>); karşılaştırma aynı biçimde yapılır.</summary>
    private static bool Same(string? stored, string? seeded)
        => string.Equals(stored?.Trim() ?? string.Empty, seeded?.Trim() ?? string.Empty, StringComparison.Ordinal);
}
