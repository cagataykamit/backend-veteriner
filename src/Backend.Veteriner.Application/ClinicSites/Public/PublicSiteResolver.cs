using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.ClinicSites.Public;

/// <summary>Adresin çözüldüğü site; sorgular bu kiracının kapsamında çalışır.</summary>
public sealed record PublicSiteScope(string Slug, PublicSiteStatus Status, ClinicSite Site, Clinic Clinic);

/// <summary>
/// Herkese açık sitenin kapısı — tek yer (ADR-045 K10/K13/K14). Adres kiracılar arası çözülür, isteğin geri kalanı o kiracıya
/// bağlanır (ADR-040). Adres bilinmiyor, kiracı ya da klinik pasif, site hiç yayınlanmamışsa sebep ayırt edilmeden 404.
/// Eski adres yeni adrese yönlenir. Kapalılık okuma anında türer: kliniğin geçici kapatması ya da yazmaya izin vermeyen abonelik
/// (salt okunur, iptal); arka plan işi yoktur, ödeme yapılınca site kendiliğinden döner. Dışarıya iki kapanma nedeni aynı görünür.
/// </summary>
public sealed class PublicSiteResolver
{
    private readonly ClinicSiteAddressBook _addressBook;
    private readonly IReadRepository<ClinicSite> _sites;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly ITenantScope _tenantScope;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _subscription;

    public PublicSiteResolver(
        ClinicSiteAddressBook addressBook,
        IReadRepository<ClinicSite> sites,
        IReadRepository<Clinic> clinics,
        IReadRepository<Tenant> tenants,
        ITenantScope tenantScope,
        TenantSubscriptionEffectiveWriteEvaluator subscription)
    {
        _addressBook = addressBook;
        _sites = sites;
        _clinics = clinics;
        _tenants = tenants;
        _tenantScope = tenantScope;
        _subscription = subscription;
    }

    public async Task<Result<PublicSiteScope>> ResolveAsync(string? requestedSlug, CancellationToken ct)
    {
        var slug = ClinicSiteSlug.Normalize(requestedSlug);
        if (!ClinicSiteSlug.IsWellFormed(slug))
            return NotFound();

        var address = await _addressBook.FindAsync(slug, ct);
        if (address is null)
            return NotFound();

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(address.TenantId), ct);
        if (tenant is not { IsActive: true })
            return NotFound();

        // İsteğin geri kalanı sitenin kiracısına bağlanır; kapsam DI kapsamıyla (istekle) biter.
        _ = _tenantScope.UseTenant(address.TenantId);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByIdSpec(address.TenantId, address.SiteId), ct);
        if (site is not { IsPublished: true })
            return NotFound();

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(address.TenantId, site.ClinicId), ct);
        if (clinic is not { IsActive: true })
            return NotFound();

        var status = await ResolveStatusAsync(slug, site, ct);
        return Result<PublicSiteScope>.Success(new PublicSiteScope(slug, status, site, clinic));
    }

    private async Task<PublicSiteStatus> ResolveStatusAsync(string slug, ClinicSite site, CancellationToken ct)
    {
        if (slug != site.Slug)
            return PublicSiteStatus.Redirect;
        if (site.IsTemporarilyClosed)
            return PublicSiteStatus.Closed;

        var subscription = await _subscription.EnsureWriteAllowedAsync(site.TenantId, ct);
        return subscription.IsSuccess ? PublicSiteStatus.Published : PublicSiteStatus.Closed;
    }

    private static Result<PublicSiteScope> NotFound() => Result<PublicSiteScope>.Failure(ClinicSiteErrors.PublicNotFound);
}
