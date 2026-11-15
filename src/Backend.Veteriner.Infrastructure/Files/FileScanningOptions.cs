namespace Backend.Veteriner.Infrastructure.Files;

/// <summary>
/// Yükleme virüs taraması (IMAGING_API_CONTRACT.md §5). Varsayılan kapalı (yerel geliştirme, testler); canlıda
/// <c>FileScanning__Enabled=true</c> ile ClamAV açılır. Kapalıyken her içerik geçer (<see cref="NoMalwareScanner"/>).
/// </summary>
public sealed class FileScanningOptions
{
    public const string SectionName = "FileScanning";

    public bool Enabled { get; set; }

    public ClamdOptions Clamd { get; set; } = new();
}

/// <summary>clamd TCP bağlantısı; sunucu kurulumu ürün deposunda <c>operations/CLAMAV-KURULUM.md</c>.</summary>
public sealed class ClamdOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 3310;

    /// <summary>Bağlantı, gönderim ve yanıtın toplam süresi.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Taranabilecek en büyük içerik; clamd <c>StreamMaxLength</c> değerinden büyük olmamalıdır (clamd varsayılanı 25 MB). Bundan
    /// büyük içerik taranamaz ve reddedilir. Yükleme sınırları bugün bunun altındadır (dosya 15 MB, içe aktarma 10 MB, site görseli 5 MB).
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 25;

    public long MaxFileBytes => (long)MaxFileSizeMb * 1024 * 1024;
}
