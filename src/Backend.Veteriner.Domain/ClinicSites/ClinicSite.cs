using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClinicSites;

/// <summary>
/// Kliniğin web sitesi (ADR-045 K2): klinik başına bir kayıt; üzerinde çalışılan taslak burada, yayınlananlar değişmez
/// <see cref="ClinicSiteVersion"/> satırlarındadır. Önceki sürüme dönüş taslağa kopyalar; yayınlamak yine kliniğin onayıyladır.
/// Geçici kapatma klinik kararıdır; abonelikten doğan kapanma burada tutulmaz, okuma anında abonelik durumundan türer (K13).
/// </summary>
public sealed class ClinicSite : ITenantOwned
{
    /// <summary>Saklanan sürüm sayısı (K2); yayında en eskisi düşer, yayındaki sürüm hep en yenisidir.</summary>
    public const int MaxRetainedVersions = 30;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }

    /// <summary>Güncel adres; eski adresler <see cref="ClinicSiteAddress"/> satırlarında kalır.</summary>
    public string Slug { get; private set; } = default!;

    public string DraftConfigJson { get; private set; } = default!;
    public DateTime DraftUpdatedAtUtc { get; private set; }

    /// <summary>Taslak yayındaki sürümden farklı olabilir ("değişiklik var, yayında değil", K9).</summary>
    public bool HasUnpublishedChanges { get; private set; }

    /// <summary>Yayındaki sürüm; hiç yayınlanmadıysa boş.</summary>
    public int? PublishedVersionNumber { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }

    /// <summary>Kliniğin elle verdiği geçici kapatma (K9).</summary>
    public bool IsTemporarilyClosed { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>SQL Server rowversion — iki sekmeden kaydın birbirini ezmesini önler.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private ClinicSite() { }

    public bool IsPublished => PublishedVersionNumber is not null;

    public static ClinicSite Create(Guid tenantId, Guid clinicId, string slug, ClinicSiteConfig draft, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        ArgumentNullException.ThrowIfNull(draft);
        if (ClinicSiteSlug.Check(slug) != ClinicSiteSlugCheck.Valid)
            throw new ArgumentException("Adres geçersiz.", nameof(slug));

        return new ClinicSite
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Slug = slug,
            DraftConfigJson = draft.Json,
            DraftUpdatedAtUtc = utcNow,
            HasUnpublishedChanges = true,
            CreatedAtUtc = utcNow,
        };
    }

    public void SaveDraft(ClinicSiteConfig draft, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.Equals(DraftConfigJson, draft.Json, StringComparison.Ordinal))
            return;

        DraftConfigJson = draft.Json;
        DraftUpdatedAtUtc = utcNow;
        HasUnpublishedChanges = true;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Eski sürümü taslağa kopyalar; sürüm ve yayın değişmez (K2).</summary>
    public void RestoreToDraft(ClinicSiteVersion version, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.SiteId != Id)
            throw new ArgumentException("Sürüm bu siteye ait değil.", nameof(version));

        DraftConfigJson = version.ConfigJson;
        DraftUpdatedAtUtc = utcNow;
        HasUnpublishedChanges = version.VersionNumber != PublishedVersionNumber;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Taslağı yeni sürüm olarak yayınlar. Zorunlu liste (K9) çağıran tarafta denetlenir; yapılandırma yeniden doğrulanır.</summary>
    public Result<ClinicSiteVersion> Publish(Guid publishedByUserId, DateTime utcNow)
    {
        var config = ClinicSiteConfig.Parse(DraftConfigJson);
        if (!config.IsSuccess)
            return Result<ClinicSiteVersion>.Failure(config.Error);

        var version = new ClinicSiteVersion(TenantId, Id, (PublishedVersionNumber ?? 0) + 1, config.Value!, publishedByUserId, utcNow);
        PublishedVersionNumber = version.VersionNumber;
        PublishedAtUtc = utcNow;
        HasUnpublishedChanges = false;
        UpdatedAtUtc = utcNow;
        return Result<ClinicSiteVersion>.Success(version);
    }

    public void ChangeSlug(string slug, DateTime utcNow)
    {
        if (ClinicSiteSlug.Check(slug) != ClinicSiteSlugCheck.Valid)
            throw new ArgumentException("Adres geçersiz.", nameof(slug));

        Slug = slug;
        UpdatedAtUtc = utcNow;
    }

    public void Close(DateTime utcNow)
    {
        IsTemporarilyClosed = true;
        UpdatedAtUtc = utcNow;
    }

    public void Reopen(DateTime utcNow)
    {
        IsTemporarilyClosed = false;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>İstemcinin gördüğü sürüm: kalıcı yazma yalnız veritabanındaki sürüm bu değere eşitse gerçekleşir.</summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }
}
