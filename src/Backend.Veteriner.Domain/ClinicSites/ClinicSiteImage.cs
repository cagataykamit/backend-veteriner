using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClinicSites;

/// <summary>
/// Herkese açık site görseli (ADR-045 K12): kiracı ve siteye bağlıdır, yalnız JPEG, PNG ve WebP kabul edilir; tür içerikten
/// (<see cref="StoredFileType.Detect"/>) belirlenir. <see cref="StoredFile"/>'dan ayrı bir varlıktır; hasta dosyalarının erişim
/// kuralı bundan etkilenmez ve o dosyalar bu yoldan açılmaz.
/// </summary>
public sealed class ClinicSiteImage : ITenantOwned
{
    public const long MaxSizeBytes = 5L * 1024 * 1024;
    public const int MaxImagesPerSite = 100;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid SiteId { get; private set; }
    public string StorageKey { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = default!;
    public Guid UploadedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Doluysa silinmiştir; satır, eski sürümlerin başvurusu bu siteye ait kalsın diye durur.</summary>
    public DateTime? DeletedAtUtc { get; private set; }

    public Guid? DeletedByUserId { get; private set; }

    private ClinicSiteImage() { }

    public bool IsDeleted => DeletedAtUtc is not null;

    /// <summary>İçerik depoya yazıldıktan sonra kayıt oluşur; anahtar kimlikten türediği için kimlik dışarıdan verilir.</summary>
    public static ClinicSiteImage Create(
        Guid id,
        Guid tenantId,
        Guid clinicId,
        Guid siteId,
        string storageKey,
        StoredFileType type,
        long sizeBytes,
        string sha256Hex,
        Guid uploadedByUserId,
        DateTime utcNow)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id geçersiz.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (siteId == Guid.Empty)
            throw new ArgumentException("SiteId geçersiz.", nameof(siteId));
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("Depolama anahtarı boş olamaz.", nameof(storageKey));
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsImage)
            throw new ArgumentException("Site görseli yalnız görsel türünde olabilir.", nameof(type));
        if (sizeBytes <= 0 || sizeBytes > MaxSizeBytes)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        if (string.IsNullOrWhiteSpace(sha256Hex) || sha256Hex.Length != StoredFile.Sha256HexLength)
            throw new ArgumentException("SHA-256 özeti geçersiz.", nameof(sha256Hex));
        if (uploadedByUserId == Guid.Empty)
            throw new ArgumentException("UploadedByUserId geçersiz.", nameof(uploadedByUserId));

        return new ClinicSiteImage
        {
            Id = id,
            TenantId = tenantId,
            ClinicId = clinicId,
            SiteId = siteId,
            StorageKey = storageKey,
            ContentType = type.ContentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256Hex.ToLowerInvariant(),
            UploadedByUserId = uploadedByUserId,
            CreatedAtUtc = utcNow,
        };
    }

    public void MarkDeleted(Guid deletedByUserId, DateTime utcNow)
    {
        if (IsDeleted)
            return;

        DeletedAtUtc = utcNow;
        DeletedByUserId = deletedByUserId;
    }
}
