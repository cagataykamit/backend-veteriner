using Ardalis.Specification;
using Backend.Veteriner.Application.ClinicSites;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.VetFinder;

namespace Backend.Veteriner.Application.VetFinder;

public sealed class ClinicVetFinderListingByClinicSpec : Specification<ClinicVetFinderListing>
{
    public ClinicVetFinderListingByClinicSpec(Guid tenantId, Guid clinicId)
        => Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);
}

/// <summary>Kiracılar arası; çağıran <c>ITenantScope.AsSystem</c> kapsamında çalıştırır (<see cref="Public.VetFinderDirectory"/>).</summary>
public sealed class ListedVetFinderListingsSpec : Specification<ClinicVetFinderListing>
{
    public ListedVetFinderListingsSpec(IReadOnlyCollection<string>? publicKeys = null)
    {
        Query.AsNoTracking();
        Query.Where(x => x.IsListed);
        if (publicKeys is not null)
            Query.Where(x => publicKeys.Contains(x.PublicKey));
    }
}

/// <summary>Kiracılar arası anahtar araması; çağıran <c>ITenantScope.AsSystem</c> kapsamında çalıştırır.</summary>
public sealed class VetFinderListingByPublicKeySpec : Specification<ClinicVetFinderListing>
{
    public VetFinderListingByPublicKeySpec(string publicKey)
    {
        Query.AsNoTracking();
        Query.Where(x => x.PublicKey == publicKey);
    }
}

public sealed class TenantSubscriptionsByTenantIdsSpec : Specification<TenantSubscription>
{
    public TenantSubscriptionsByTenantIdsSpec(IReadOnlyCollection<Guid> tenantIds)
    {
        Query.AsNoTracking();
        Query.Where(x => tenantIds.Contains(x.TenantId));
    }
}

/// <summary>Kartın okuduğu klinik alanları; yalnız aktif klinik.</summary>
public sealed record VetFinderClinicRecord(Guid Id, Guid TenantId, string Name, string City, string? District, string? Address, string? Phone);

public sealed class VetFinderClinicsByIdsSpec : Specification<Clinic, VetFinderClinicRecord>
{
    public VetFinderClinicsByIdsSpec(IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(x => x.IsActive && clinicIds.Contains(x.Id));
        Query.Select(x => new VetFinderClinicRecord(x.Id, x.TenantId, x.Name, x.City, x.District, x.Address, x.Phone));
    }
}

public sealed class ClinicWorkingHoursByClinicIdsSpec : Specification<ClinicWorkingHour>
{
    public ClinicWorkingHoursByClinicIdsSpec(IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(x => clinicIds.Contains(x.ClinicId)).OrderBy(x => x.DayOfWeek);
    }
}

/// <summary>Kartın okuduğu online randevu alanları.</summary>
public sealed record VetFinderBookingRecord(Guid TenantId, Guid ClinicId, bool IsEnabled, bool ShowNextAvailableSlots, string BookingCode);

public sealed class VetFinderBookingByClinicIdsSpec : Specification<ClinicOnlineBookingSettings, VetFinderBookingRecord>
{
    public VetFinderBookingByClinicIdsSpec(IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(x => clinicIds.Contains(x.ClinicId));
        Query.Select(x => new VetFinderBookingRecord(x.TenantId, x.ClinicId, x.IsEnabled, x.ShowNextAvailableSlots, x.BookingCode));
    }
}

/// <summary>Yayında ve geçici kapalı olmayan siteler; taslak ve yapılandırma okunmaz.</summary>
public sealed class OpenPublicSitesByClinicIdsSpec : Specification<ClinicSite, PublicSiteRecord>
{
    public OpenPublicSitesByClinicIdsSpec(IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(x => clinicIds.Contains(x.ClinicId) && x.PublishedVersionNumber != null && !x.IsTemporarilyClosed);
        Query.Select(x => new PublicSiteRecord(x.Id, x.TenantId, x.ClinicId, x.Slug, x.IsTemporarilyClosed, x.PublishedVersionNumber));
    }
}
