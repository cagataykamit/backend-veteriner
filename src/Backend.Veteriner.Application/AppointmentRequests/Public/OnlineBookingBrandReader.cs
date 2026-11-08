using Backend.Veteriner.Application.Clinics.OnlineBooking;
using Backend.Veteriner.Application.ClinicSites;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.ClinicSites;

namespace Backend.Veteriner.Application.AppointmentRequests.Public;

/// <summary>Randevu sayfasının markası: rengi ve logosu (görsel kaydı; içerik ayrı uçtan açılır).</summary>
public sealed record OnlineBookingBrand(string? AccentColor, ClinicSiteImage? Logo);

/// <summary>
/// Kliniğe özel bağlantı sayfasının markası — tek yer (APPT-022). Kaynak kliniğin sitesidir (ADR-045); yalnız yayında ve geçici
/// kapalı olmayan sitenin yayındaki sürümü okunur, taslak hiç okunmaz. Logo yayındaki sürümün başvurduğu, silinmemiş görsel
/// olmalıdır (herkese açık görsel ucuyla aynı kural). Abonelik ve klinik kapısı bağlantı kapısında
/// (<see cref="OnlineBookingClinicResolver"/>) geçilmiş olur. Site yoksa marka boştur, sayfa Vetinity görünümünde açılır.
/// </summary>
public sealed class OnlineBookingBrandReader
{
    private static readonly OnlineBookingBrand None = new(null, null);

    private readonly IReadRepository<ClinicSite> _sites;
    private readonly IReadRepository<ClinicSiteVersion> _versions;
    private readonly IReadRepository<ClinicSiteImage> _images;

    public OnlineBookingBrandReader(
        IReadRepository<ClinicSite> sites,
        IReadRepository<ClinicSiteVersion> versions,
        IReadRepository<ClinicSiteImage> images)
    {
        _sites = sites;
        _versions = versions;
        _images = images;
    }

    public async Task<OnlineBookingBrand> ReadAsync(OnlineBookingScope scope, CancellationToken ct)
    {
        var site = await _sites.FirstOrDefaultAsync(new PublicSiteRecordByClinicSpec(scope.TenantId, scope.ClinicId), ct);
        if (site is not { PublishedVersionNumber: { } versionNumber, IsTemporarilyClosed: false })
            return None;

        var version = await _versions.FirstOrDefaultAsync(new ClinicSiteVersionByNumberSpec(site.TenantId, site.Id, versionNumber), ct);
        if (version is null)
            return None;

        var brand = ClinicSiteBrand.Read(version.ConfigJson);
        var logo = brand.LogoImageId is { } logoId && version.ReferencesImage(logoId)
            ? await _images.FirstOrDefaultAsync(new ClinicSiteImageByIdSpec(site.TenantId, site.Id, logoId), ct)
            : null;
        return new OnlineBookingBrand(brand.AccentColor, logo);
    }
}
