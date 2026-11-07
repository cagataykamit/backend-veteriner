using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.ClinicSites;

/// <summary>
/// Adres kuralının tek yeri (ADR-045 K10). Adres tüm kiracılarda benzersiz olduğu için arama kiracılar arasıdır (ADR-040);
/// bulunan satırın yalnız sahibi ve durumu kullanılır, başka kiracının bilgisi yanıta çıkmaz.
/// </summary>
public sealed class ClinicSiteAddressBook
{
    private const int MaxSuffixAttempts = 50;

    private readonly IRepository<ClinicSiteAddress> _addresses;
    private readonly ITenantScope _tenantScope;

    public ClinicSiteAddressBook(IRepository<ClinicSiteAddress> addresses, ITenantScope tenantScope)
    {
        _addresses = addresses;
        _tenantScope = tenantScope;
    }

    /// <summary>Tüm kiracılarda arama; güncel ya da eski adres satırı.</summary>
    public async Task<ClinicSiteAddress?> FindAsync(string slug, CancellationToken ct)
    {
        using var system = _tenantScope.AsSystem();
        return await _addresses.FirstOrDefaultAsync(new ClinicSiteAddressBySlugSpec(slug), ct);
    }

    /// <summary>Uygunluk; sitenin kendi güncel ya da eski adresi uygundur.</summary>
    public async Task<ClinicSiteAddressAvailabilityDto> CheckAsync(Guid? ownSiteId, string? requested, CancellationToken ct)
    {
        var slug = ClinicSiteSlug.Normalize(requested);
        var format = ClinicSiteSlug.Check(slug);
        if (format != ClinicSiteSlugCheck.Valid)
        {
            var reason = format == ClinicSiteSlugCheck.Reserved
                ? ClinicSiteAddressUnavailableReason.Reserved
                : ClinicSiteAddressUnavailableReason.Invalid;
            return new ClinicSiteAddressAvailabilityDto(slug, false, reason);
        }

        var existing = await FindAsync(slug, ct);
        return existing is null || existing.SiteId == ownSiteId
            ? new ClinicSiteAddressAvailabilityDto(slug, true, null)
            : new ClinicSiteAddressAvailabilityDto(slug, false, ClinicSiteAddressUnavailableReason.Taken);
    }

    /// <summary>Klinik adından boş bir adres; alınmışsa sıra numarası eklenir, yarışta benzersiz indeks son sözü söyler.</summary>
    public async Task<string> SuggestAsync(string clinicName, CancellationToken ct)
    {
        var slug = ClinicSiteSlug.Suggest(clinicName);
        if (await FindAsync(slug, ct) is null)
            return slug;

        for (var suffix = 2; suffix <= MaxSuffixAttempts; suffix++)
        {
            var candidate = ClinicSiteSlug.WithSuffix(slug, suffix.ToString());
            if (await FindAsync(candidate, ct) is null)
                return candidate;
        }

        return ClinicSiteSlug.WithSuffix(slug, Guid.NewGuid().ToString("N")[..8]);
    }

    /// <summary>
    /// Adresi değiştirir; kayıt çağıranda. Yayınlanmış sitenin önceki adresi eski adres olur (yönlenir, başkasına verilmez).
    /// Hiç yayınlanmamış sitenin adresi bağlantı ya da QR olarak paylaşılmadığı için serbest kalır.
    /// </summary>
    public async Task<Result> ChangeAsync(ClinicSite site, string? requested, DateTime utcNow, CancellationToken ct)
    {
        var slug = ClinicSiteSlug.Normalize(requested);
        var format = ClinicSiteSlug.Check(slug);
        if (format != ClinicSiteSlugCheck.Valid)
            return Result.Failure(ClinicSiteErrors.InvalidSlug(format));
        if (slug == site.Slug)
            return Result.Success();

        var own = await _addresses.ListAsync(new ClinicSiteAddressesBySiteSpec(site.TenantId, site.Id), ct);
        var target = own.FirstOrDefault(a => a.Slug == slug);
        if (target is null && await FindAsync(slug, ct) is not null)
            return Result.Failure(ClinicSiteErrors.SlugTaken);

        var changed = site.ChangeSlug(slug, utcNow);
        if (!changed.IsSuccess)
            return changed;

        var current = own.FirstOrDefault(a => a.RetiredAtUtc is null);
        if (current is not null)
        {
            if (site.IsPublished)
                current.Retire(utcNow);
            else
                await _addresses.DeleteAsync(current, ct);
        }

        if (target is null)
            await _addresses.AddAsync(ClinicSiteAddress.Create(site.TenantId, site.Id, slug, utcNow), ct);
        else
            target.Reactivate();

        return Result.Success();
    }
}
