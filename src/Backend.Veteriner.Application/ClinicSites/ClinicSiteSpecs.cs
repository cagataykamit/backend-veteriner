using Ardalis.Specification;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.ClinicSites;

public sealed class ClinicSiteByClinicSpec : Specification<ClinicSite>
{
    public ClinicSiteByClinicSpec(Guid tenantId, Guid clinicId)
        => Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);
}

/// <summary>İstemcinin gönderdiği <c>rowVersion</c> ile yazılacak kayıt: izlenmez, <c>Update</c> beklenen sürümle eklenir.</summary>
public sealed class ClinicSiteByClinicUntrackedSpec : Specification<ClinicSite>
{
    public ClinicSiteByClinicUntrackedSpec(Guid tenantId, Guid clinicId)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);
    }
}

public sealed class ClinicSiteByIdSpec : Specification<ClinicSite>
{
    public ClinicSiteByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}

/// <summary>Kiracılar arası adres araması; çağıran <c>ITenantScope.AsSystem</c> kapsamında çalıştırır.</summary>
public sealed class ClinicSiteAddressBySlugSpec : Specification<ClinicSiteAddress>
{
    public ClinicSiteAddressBySlugSpec(string slug)
    {
        Query.AsNoTracking();
        Query.Where(x => x.Slug == slug);
    }
}

public sealed class ClinicSiteAddressesBySiteSpec : Specification<ClinicSiteAddress>
{
    public ClinicSiteAddressesBySiteSpec(Guid tenantId, Guid siteId)
        => Query.Where(x => x.TenantId == tenantId && x.SiteId == siteId);
}

public sealed class ClinicSiteVersionByNumberSpec : Specification<ClinicSiteVersion>
{
    public ClinicSiteVersionByNumberSpec(Guid tenantId, Guid siteId, int versionNumber)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.SiteId == siteId && x.VersionNumber == versionNumber);
    }
}

public sealed record ClinicSiteVersionSummary(int VersionNumber, DateTime PublishedAtUtc, Guid PublishedByUserId);

/// <summary>Sürüm geçmişi, en yeni önce; yapılandırma yüklenmez.</summary>
public sealed class ClinicSiteVersionSummariesSpec : Specification<ClinicSiteVersion, ClinicSiteVersionSummary>
{
    public ClinicSiteVersionSummariesSpec(Guid tenantId, Guid siteId)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.SiteId == siteId).OrderByDescending(x => x.VersionNumber);
        Query.Select(x => new ClinicSiteVersionSummary(x.VersionNumber, x.PublishedAtUtc, x.PublishedByUserId));
    }
}

/// <summary>Sınırı aşan en eski sürümler (K2): en yeni <paramref name="keep"/> satırdan sonrası.</summary>
public sealed class ClinicSiteVersionsBeyondLimitSpec : Specification<ClinicSiteVersion>
{
    public ClinicSiteVersionsBeyondLimitSpec(Guid tenantId, Guid siteId, int keep)
    {
        Query.Where(x => x.TenantId == tenantId && x.SiteId == siteId).OrderByDescending(x => x.VersionNumber);
        Query.Skip(keep);
    }
}

/// <summary>Sitenin silinmemiş görselleri, eklenme sırasıyla.</summary>
public sealed class ClinicSiteImagesBySiteSpec : Specification<ClinicSiteImage>
{
    public ClinicSiteImagesBySiteSpec(Guid tenantId, Guid siteId)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.SiteId == siteId && x.DeletedAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc);
    }
}

/// <summary>Taslak başvuruları bu kümeyle denetlenir: silinmiş görsel de siteye aittir, yayına çıkamaz ama taslağı kilitlemez.</summary>
public sealed class ClinicSiteImageIdsBySiteSpec : Specification<ClinicSiteImage, Guid>
{
    public ClinicSiteImageIdsBySiteSpec(Guid tenantId, Guid siteId)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.SiteId == siteId);
        Query.Select(x => x.Id);
    }
}

public sealed class ClinicSiteImageByIdSpec : Specification<ClinicSiteImage>
{
    public ClinicSiteImageByIdSpec(Guid tenantId, Guid siteId, Guid imageId)
        => Query.Where(x => x.TenantId == tenantId && x.SiteId == siteId && x.Id == imageId && x.DeletedAtUtc == null);
}

public sealed record ClinicSiteVersionPublisher(Guid Id, string? DisplayName, string Email);

public sealed class ClinicSiteVersionPublishersSpec : Specification<User, ClinicSiteVersionPublisher>
{
    public ClinicSiteVersionPublishersSpec(IReadOnlyCollection<Guid> userIds)
    {
        Query.AsNoTracking();
        Query.Where(u => userIds.Contains(u.Id));
        Query.Select(u => new ClinicSiteVersionPublisher(u.Id, u.DisplayName, u.Email));
    }
}
