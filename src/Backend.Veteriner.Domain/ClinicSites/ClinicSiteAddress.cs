using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClinicSites;

/// <summary>
/// Adres defteri (ADR-045 K10): sitenin güncel ve eski adresleri. <see cref="Slug"/> tüm kiracılarda benzersizdir, bu yüzden
/// yayınlanmış bir sitenin eski adresi başkasına verilemez ve yeni adrese çözülür. Site kendi eski adresine dönebilir.
/// </summary>
public sealed class ClinicSiteAddress : ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid SiteId { get; private set; }
    public string Slug { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Doluysa eski adrestir; boşsa sitenin güncel adresi.</summary>
    public DateTime? RetiredAtUtc { get; private set; }

    private ClinicSiteAddress() { }

    public static ClinicSiteAddress Create(Guid tenantId, Guid siteId, string slug, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (siteId == Guid.Empty)
            throw new ArgumentException("SiteId geçersiz.", nameof(siteId));
        if (ClinicSiteSlug.Check(slug) != ClinicSiteSlugCheck.Valid)
            throw new ArgumentException("Adres geçersiz.", nameof(slug));

        return new ClinicSiteAddress { TenantId = tenantId, SiteId = siteId, Slug = slug, CreatedAtUtc = utcNow };
    }

    public void Retire(DateTime utcNow) => RetiredAtUtc = utcNow;

    public void Reactivate() => RetiredAtUtc = null;
}
