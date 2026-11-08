using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.VetFinder;

namespace Backend.Veteriner.Application.VetFinder.Public;

/// <summary>
/// Listelenen bir klinik: kartın ve filtrelerin okuduğu her şey. Kiracı ve klinik kimliği yalnız sunucu içindir, yanıta çıkmaz.
/// İl ve ilçe <see cref="TurkishSlug"/> biçiminde de taşınır; ilçesi olmayan klinik yalnız il sayfasında görünür.
/// </summary>
public sealed record VetFinderEntry(
    Guid TenantId,
    Guid ClinicId,
    string Key,
    string Name,
    string City,
    string CitySlug,
    string? District,
    string? DistrictSlug,
    string? Address,
    string? Phone,
    IReadOnlyList<string> Services,
    GeoPoint? Location,
    IReadOnlyList<ClinicWorkingHour> Hours,
    string? BookingCode,
    bool PublishesNextSlots,
    string? SiteSlug);

/// <summary>Anahtarla bulunan listelenen klinik (en yakın saat ucu).</summary>
public sealed record VetFinderListedClinic(string Key, Guid TenantId, Guid ClinicId);

/// <summary>
/// Veteriner bul kapısı — tek yer (ADR-047 K4, ADR-040). Liste tüm kiracılardan okunur: yalnız klinik listelenmeyi açmışsa, kiracı
/// ve klinik aktifse ve abonelik yazmaya izin veriyorsa (deneme ya da ödenmiş dönem; salt okunur ve iptal listelenmez, K13 ile aynı
/// ilke). Durum her istekte kayıttan türer, önbellek yoktur: klinik kapatınca ya da abonelik kapanınca hemen çıkar. Her satırın
/// kiracısı ilişkili kayıtlarla ayrıca karşılaştırılır; başka kiracının saati, ayarı ya da sitesi bir karta karışmaz.
/// </summary>
public sealed class VetFinderDirectory
{
    private readonly IReadRepository<ClinicVetFinderListing> _listings;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicWorkingHour> _hours;
    private readonly IReadRepository<ClinicOnlineBookingSettings> _booking;
    private readonly IReadRepository<ClinicSite> _sites;
    private readonly ITenantScope _tenantScope;
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly TimeProvider _timeProvider;

    public VetFinderDirectory(
        IReadRepository<ClinicVetFinderListing> listings,
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicWorkingHour> hours,
        IReadRepository<ClinicOnlineBookingSettings> booking,
        IReadRepository<ClinicSite> sites,
        ITenantScope tenantScope,
        IOwnerPortalSecretGenerator secrets,
        TimeProvider timeProvider)
    {
        _listings = listings;
        _tenants = tenants;
        _subscriptions = subscriptions;
        _clinics = clinics;
        _hours = hours;
        _booking = booking;
        _sites = sites;
        _tenantScope = tenantScope;
        _secrets = secrets;
        _timeProvider = timeProvider;
    }

    /// <summary>Listelenen bütün klinikler.</summary>
    public async Task<IReadOnlyList<VetFinderEntry>> LoadAsync(CancellationToken ct)
    {
        using var system = _tenantScope.AsSystem();

        var listings = await EligibleListingsAsync(null, ct);
        if (listings.Count == 0)
            return [];

        var clinicIds = listings.Select(l => l.ClinicId).ToList();
        var clinics = (await _clinics.ListAsync(new VetFinderClinicsByIdsSpec(clinicIds), ct)).ToDictionary(c => c.Id);
        var hours = (await _hours.ListAsync(new ClinicWorkingHoursByClinicIdsSpec(clinicIds), ct)).ToLookup(h => h.ClinicId);
        var booking = (await _booking.ListAsync(new VetFinderBookingByClinicIdsSpec(clinicIds), ct)).ToDictionary(b => b.ClinicId);
        var sites = (await _sites.ListAsync(new OpenPublicSitesByClinicIdsSpec(clinicIds), ct)).ToDictionary(s => s.ClinicId);

        var entries = new List<VetFinderEntry>(listings.Count);
        foreach (var listing in listings)
        {
            if (!clinics.TryGetValue(listing.ClinicId, out var clinic) || clinic.TenantId != listing.TenantId)
                continue;

            var settings = booking.GetValueOrDefault(listing.ClinicId) is { } b && b.TenantId == listing.TenantId && b.IsEnabled ? b : null;
            var site = sites.GetValueOrDefault(listing.ClinicId) is { } s && s.TenantId == listing.TenantId ? s : null;
            var districtSlug = TurkishSlug.From(clinic.District);

            entries.Add(new VetFinderEntry(
                listing.TenantId,
                listing.ClinicId,
                listing.PublicKey,
                clinic.Name,
                clinic.City,
                TurkishSlug.From(clinic.City),
                districtSlug.Length == 0 ? null : clinic.District,
                districtSlug.Length == 0 ? null : districtSlug,
                clinic.Address,
                clinic.Phone,
                listing.Services,
                listing is { Latitude: { } lat, Longitude: { } lng } ? new GeoPoint((double)lat, (double)lng) : null,
                hours[listing.ClinicId].Where(h => h.TenantId == listing.TenantId).ToList(),
                settings?.BookingCode,
                settings is { ShowNextAvailableSlots: true },
                site?.Slug));
        }

        return entries;
    }

    /// <summary>Anahtarlarla listelenen klinikler; bilinmeyen ya da listelenmeyen anahtar dönmez.</summary>
    public async Task<IReadOnlyList<VetFinderListedClinic>> FindListedAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        using var system = _tenantScope.AsSystem();
        return (await EligibleListingsAsync(keys, ct))
            .Select(l => new VetFinderListedClinic(l.PublicKey, l.TenantId, l.ClinicId))
            .ToList();
    }

    /// <summary>Yeni anahtar: tahmin edilemez, karışmayan küçük harf/rakam; çakışma olasılığı ihmal edilebilir, yine de denetlenir.</summary>
    public async Task<string> NewUniqueKeyAsync(CancellationToken ct)
    {
        using var system = _tenantScope.AsSystem();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var key = _secrets.CreateLinkCode(ClinicVetFinderListing.PublicKeyLength);
            if (!await _listings.AnyAsync(new VetFinderListingByPublicKeySpec(key), ct))
                return key;
        }

        throw new InvalidOperationException("Benzersiz Veteriner bul anahtarı üretilemedi.");
    }

    /// <summary>Listelenmeyi açmış, kiracısı aktif ve aboneliği yazmaya izin veren kayıtlar (klinik aktifliği çağıranda).</summary>
    private async Task<List<ClinicVetFinderListing>> EligibleListingsAsync(IReadOnlyCollection<string>? keys, CancellationToken ct)
    {
        var listings = await _listings.ListAsync(new ListedVetFinderListingsSpec(keys), ct);
        if (listings.Count == 0)
            return [];

        var tenantIds = listings.Select(l => l.TenantId).Distinct().ToList();
        var activeTenants = (await _tenants.ListAsync(new PortalTenantsByIdsSpec(tenantIds), ct))
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToHashSet();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var writable = (await _subscriptions.ListAsync(new TenantSubscriptionsByTenantIdsSpec(tenantIds), ct))
            .Where(s => TenantSubscriptionEffectiveWriteEvaluator.WriteAllowed(TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(s, utcNow)))
            .Select(s => s.TenantId)
            .ToHashSet();

        return listings.Where(l => activeTenants.Contains(l.TenantId) && writable.Contains(l.TenantId)).ToList();
    }
}
