using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Sistem (global) muayene şablonlarını idempotent ekler (ADR-020 S4). <c>TenantId</c> null; Türkçe ad anahtarı
/// (<see cref="TurkishTextKey"/>) üzerinden tekrar engellenir; mevcut satırları değiştirmez/silmez.
/// İçerik az sayıda ve geneldir: yalnız metin, olgu bildirmeyen boş başlıklar (hekim doldurur; "yok/normal" gibi hazır
/// sonuç yazılmaz); kiracıdan bağımsız ürün bağı uydurulmaz, klinik kendi şablonunda ürün/hizmet ekler.
/// Pilotta kliniklerle doğrulanır.
/// </summary>
public static class ExaminationTemplateSeeder
{
    public sealed record SystemTemplate(string Name, string? VisitReason, string? Findings, string? Plan);

    public static readonly IReadOnlyList<SystemTemplate> Templates =
    [
        new(
            "Rutin muayene",
            "Genel kontrol",
            "Genel durum: \nMukozalar / KDZ: \nLenf yumruları: \nKalp-akciğer oskültasyonu: \nKarın palpasyonu: \nDeri / tüy: \nGöz / kulak / ağız: ",
            null),
        new(
            "Aşı muayenesi",
            "Aşı",
            "Genel durum: \nVücut ısısı ve iştah: \nAşıya engel bulgu: ",
            "Aşı sonrası gözlem: \nSonraki doz tarihi: "),
        new(
            "Kontrol muayenesi",
            "Kontrol",
            "Önceki şikâyetin seyri: \nTedaviye yanıt: \nGenel durum: ",
            "Tedaviye devam / sonlandırma kararı: \nSonraki kontrol: "),
    ];

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
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
                ExaminationTemplate.CreateSystem(template.Name, template.VisitReason, template.Findings, template.Plan), ct);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("ExaminationTemplateSeeder: {Count} system examination template(s) added.", added);
        }
    }
}
