using System.Text;

namespace Backend.Veteriner.Domain.Files;

/// <summary>
/// Kullanıcının verdiği dosya adını yalnız gösterim için temizler (diskte ad kullanılmaz): dizin kısmı atılır,
/// denetim karakterleri ve yol/kabuk karakterleri çıkarılır, baştaki/sondaki nokta ve boşluk kırpılır, uzunluk sınırlanır.
/// </summary>
public static class StoredFileName
{
    public const int MaxLength = 120;
    public const string Fallback = "dosya";

    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Fallback;

        var name = fileName.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];

        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            if (char.IsControl(c) || Array.IndexOf(Forbidden, c) >= 0)
                continue;
            builder.Append(c);
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        if (cleaned.Length > MaxLength)
        {
            var extension = Path.GetExtension(cleaned);
            if (extension.Length is > 0 and <= 10)
                cleaned = cleaned[..(MaxLength - extension.Length)].TrimEnd() + extension;
            else
                cleaned = cleaned[..MaxLength].TrimEnd();
        }

        return cleaned.Length == 0 ? Fallback : cleaned;
    }

    /// <summary>
    /// İndirme adı: temizlenmiş adın uzantısı, içerikten tespit edilen türün uzantısıyla değiştirilir
    /// (ör. <c>röntgen.exe</c> adlı JPEG <c>röntgen.jpg</c> iner).
    /// </summary>
    public static string ForDownload(string storedName, StoredFileType type)
    {
        var baseName = Path.GetFileNameWithoutExtension(storedName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = Fallback;
        return baseName + type.Extension;
    }
}
