using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Examinations;

/// <summary>
/// Şablon metnini muayene alanına birleştirme kuralı (ADR-020 S4; tek yer). Mevcut içerik asla silinmez veya değiştirilmez:
/// <list type="number">
/// <item>Eklenecek metin boşsa alan aynen kalır.</item>
/// <item>Alan boşsa eklenecek metin yazılır.</item>
/// <item>Alan eklenecek metni zaten içeriyorsa (Türkçe harf, büyük/küçük ve boşluk farkı yok sayılır, <see cref="TurkishTextKey"/>)
/// alan aynen kalır; aynı şablonu ikinci kez uygulamak metni çoğaltmaz.</item>
/// <item>Aksi halde mevcut metnin sonuna ayraçla eklenir: kısa alan (şikâyet) <see cref="InlineSeparator"/>,
/// uzun alanlar (bulgu, plan) <see cref="BlockSeparator"/>.</item>
/// </list>
/// </summary>
public static class ExaminationTextMerge
{
    public const string InlineSeparator = ", ";
    public const string BlockSeparator = "\n";

    public static string? Merge(string? current, string? addition, string separator)
    {
        if (string.IsNullOrWhiteSpace(addition))
            return current;

        var text = addition.Trim();
        if (string.IsNullOrWhiteSpace(current))
            return text;

        if (TurkishTextKey.Normalize(current).Contains(TurkishTextKey.Normalize(text), StringComparison.Ordinal))
            return current;

        return current.TrimEnd() + separator + text;
    }
}
