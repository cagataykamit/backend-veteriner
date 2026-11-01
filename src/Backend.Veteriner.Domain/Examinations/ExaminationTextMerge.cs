using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Examinations;

/// <summary>
/// Şablon metnini muayene alanına birleştirme kuralı (ADR-020 S4; tek yer). Mevcut içerik asla silinmez veya değiştirilmez.
/// <list type="bullet">
/// <item><b>Kısa alan (şikâyet)</b> — <see cref="FillIfEmpty"/>: alan boşsa şablon metni yazılır; doluysa şablonun metni
/// <b>eklenmez</b> (ziyaret sebebi cümledir; sonuna virgülle başka sebep eklenince anlamsızlaşır).</item>
/// <item><b>Uzun alanlar (bulgu, plan)</b> — <see cref="MergeLines"/>: şablon metni <b>satır satır</b> sona eklenir; alanda zaten
/// bulunan satır tekrar eklenmez. Karşılaştırma <see cref="TurkishTextKey"/> ile (baş/son ve ardışık boşluk, büyük/küçük
/// harf ve Türkçe harf farkı yok sayılır). Şablon satırı başlıksa (":" ile biter) ve muayenede o başlıkla başlayan,
/// hekimin doldurduğu bir satır varsa ("Genel durum: iyi") başlık yine tekrar eklenmez. Böylece "Genel durum:" içeren
/// iki şablon arka arkaya uygulanınca başlık çoğalmaz; aynı şablonu ikinci kez uygulamak hiçbir satır eklemez.</item>
/// </list>
/// </summary>
public static class ExaminationTextMerge
{
    public const string BlockSeparator = "\n";
    private const char HeadingSuffix = ':';

    /// <summary>Kısa alan: boşsa doldurur. <c>Filled</c> false ise alan aynen döner (şablon metni boş veya alan dolu).</summary>
    public static (string? Text, bool Filled) FillIfEmpty(string? current, string? addition)
    {
        if (string.IsNullOrWhiteSpace(addition) || !string.IsNullOrWhiteSpace(current))
            return (current, false);

        return (addition.Trim(), true);
    }

    /// <summary>Uzun alan: birleşmiş metni ve eklenen satır sayısını döner; eklenecek satır yoksa alan aynen döner.</summary>
    public static (string? Text, int AddedLines) MergeLines(string? current, string? addition)
    {
        if (string.IsNullOrWhiteSpace(addition))
            return (current, 0);

        var existing = SplitLines(current).Select(TurkishTextKey.Normalize).ToList();
        var added = new List<string>();
        foreach (var line in SplitLines(addition))
        {
            var key = TurkishTextKey.Normalize(line);
            if (existing.Any(e => SameLine(e, key)))
                continue;

            existing.Add(key);
            added.Add(line.Trim());
        }

        if (added.Count == 0)
            return (current, 0);

        var appended = string.Join(BlockSeparator, added);
        var text = string.IsNullOrWhiteSpace(current) ? appended : current.TrimEnd() + BlockSeparator + appended;
        return (text, added.Count);
    }

    private static bool SameLine(string existingKey, string templateKey)
        => existingKey.Equals(templateKey, StringComparison.Ordinal)
           || (templateKey.EndsWith(HeadingSuffix) && existingKey.StartsWith(templateKey, StringComparison.Ordinal));

    private static IEnumerable<string> SplitLines(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l));
}
