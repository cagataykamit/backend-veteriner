namespace Backend.Veteriner.Domain.Files;

/// <summary>
/// İzinli dosya türleri (ASAMA6 I2/D7): yalnız JPEG, PNG, WebP ve PDF. Tür uzantıdan veya istemcinin bildirdiği
/// içerik türünden değil, içeriğin ilk baytlarından (magic bytes) belirlenir; tek kural <see cref="Detect"/>.
/// </summary>
public sealed record StoredFileType(string ContentType, string Extension, bool IsImage)
{
    /// <summary><see cref="Detect"/> için okunması gereken en az bayt sayısı.</summary>
    public const int SignatureLength = 12;

    public static readonly StoredFileType Jpeg = new("image/jpeg", ".jpg", true);
    public static readonly StoredFileType Png = new("image/png", ".png", true);
    public static readonly StoredFileType Webp = new("image/webp", ".webp", true);
    public static readonly StoredFileType Pdf = new("application/pdf", ".pdf", false);

    private static readonly StoredFileType[] All = [Jpeg, Png, Webp, Pdf];

    private static ReadOnlySpan<byte> JpegMagic => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> RiffMagic => "RIFF"u8;
    private static ReadOnlySpan<byte> WebpMagic => "WEBP"u8;
    private static ReadOnlySpan<byte> PdfMagic => "%PDF-"u8;

    /// <summary>İçeriğin başından türü belirler; izinli türlerden biri değilse <c>null</c>.</summary>
    public static StoredFileType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegMagic))
            return Jpeg;
        if (header.StartsWith(PngMagic))
            return Png;
        if (header.Length >= 12 && header.StartsWith(RiffMagic) && header.Slice(8, 4).SequenceEqual(WebpMagic))
            return Webp;
        if (header.StartsWith(PdfMagic))
            return Pdf;
        return null;
    }

    /// <summary>Kayıtlı içerik türünden tanımı döner (yalnız izinli türler).</summary>
    public static StoredFileType? FromContentType(string? contentType)
        => All.FirstOrDefault(t => string.Equals(t.ContentType, contentType, StringComparison.OrdinalIgnoreCase));
}
