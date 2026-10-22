namespace Backend.Veteriner.Application.Files;

/// <summary>Dosya katmanı ayarları (<c>FileStorage</c>; IMAGING_API_CONTRACT.md §2, §5, §6).</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Yapılandırılabilir dosya sınırının tavanı; uç düzeyi gövde sınırı buna göre sabittir.</summary>
    public const long MaxFileBytesCeiling = 50L * 1024 * 1024;

    /// <summary>Yükleme uçlarının istek gövdesi üst sınırı (taşıma katmanı; multipart ek yükü için +1 MB).</summary>
    public const long MaxRequestBytes = MaxFileBytesCeiling + 1024 * 1024;

    /// <summary>Depolama kök dizini. Boşsa yükleme ve indirme 503 döner; göreli yol uygulama dizinine göre çözülür.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Dosya başına en çok bayt (varsayılan 15 MB, en çok <see cref="MaxFileBytesCeiling"/>).</summary>
    public long MaxFileBytes { get; set; } = 15L * 1024 * 1024;

    /// <summary>Sahip kayıt başına en çok yüklenen/hazır dosya.</summary>
    public int MaxFilesPerOwner { get; set; } = 20;

    /// <summary>İmzalı erişim URL'si ömrü (saniye, 30–3600).</summary>
    public int AccessUrlLifetimeSeconds { get; set; } = 300;

    /// <summary>İmza anahtarı (Base64, en az 32 bayt). Boşsa süreç başına rastgele anahtar kullanılır. Gizli bilgidir.</summary>
    public string AccessUrlSigningKey { get; set; } = string.Empty;

    public long EffectiveMaxFileBytes => Math.Clamp(MaxFileBytes, 1, MaxFileBytesCeiling);

    public TimeSpan EffectiveAccessUrlLifetime => TimeSpan.FromSeconds(Math.Clamp(AccessUrlLifetimeSeconds, 30, 3600));
}
