using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClinicSites;

/// <summary>
/// Değişmez yayın sürümü (ADR-045 K2): sıra numarası, yapılandırma, yayınlayan ve zaman. Oluştuktan sonra alan değişmez;
/// yalnız <see cref="ClinicSite.MaxRetainedVersions"/> aşılınca en eskisi silinir. Oluşturma yolu <see cref="ClinicSite.Publish"/>.
/// </summary>
public sealed class ClinicSiteVersion : ITenantOwned
{
    private const char ImageIdSeparator = ',';

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid SiteId { get; private set; }
    public int VersionNumber { get; private set; }
    public string ConfigJson { get; private set; } = default!;

    /// <summary>
    /// Yapılandırmanın başvurduğu görseller, virgülle ayrılı. Herkese açık görsel ucu her istekte belgeyi ayrıştırmadan
    /// "bu görsel yayında mı" sorusunu buradan yanıtlar.
    /// </summary>
    public string ReferencedImageIds { get; private set; } = default!;

    public Guid PublishedByUserId { get; private set; }
    public DateTime PublishedAtUtc { get; private set; }

    private ClinicSiteVersion() { }

    internal ClinicSiteVersion(Guid tenantId, Guid siteId, int versionNumber, ClinicSiteConfig config, Guid publishedByUserId, DateTime utcNow)
    {
        if (publishedByUserId == Guid.Empty)
            throw new ArgumentException("PublishedByUserId geçersiz.", nameof(publishedByUserId));

        TenantId = tenantId;
        SiteId = siteId;
        VersionNumber = versionNumber;
        ConfigJson = config.Json;
        ReferencedImageIds = string.Join(ImageIdSeparator, config.ImageIds.Select(id => id.ToString("N")));
        PublishedByUserId = publishedByUserId;
        PublishedAtUtc = utcNow;
    }

    public bool ReferencesImage(Guid imageId) => ListContainsImage(ReferencedImageIds, imageId);

    /// <summary>
    /// Saklanan listede arama; kimlikler eşit uzunlukta ve ayraçlı olduğundan alt dize araması tam eşleşmedir. Herkese açık görsel
    /// ucu yalnız bu sütunu okur, yapılandırma yüklenmez.
    /// </summary>
    public static bool ListContainsImage(string referencedImageIds, Guid imageId)
        => referencedImageIds.Contains(imageId.ToString("N"), StringComparison.Ordinal);
}
